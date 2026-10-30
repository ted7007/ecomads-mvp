using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

public partial class ResultsEndpointTests
{
    private static async Task<RecommendationResponse> NewReportAsync(EcomadsDbContext db, Seed seed, Clock clock,
        RecommendationCard? card = null, DateOnly? end = null)
    {
        var report = JsonSerializer.Deserialize<RecommendationResponse>(seed.Run.SnapshotJson, ResultsService.Json)!;
        var run = new WbRecommendationRun { Id = Guid.NewGuid(), SellerId = seed.Seller.Id, StoreId = seed.Store.Id,
            CampaignId = seed.Campaign.Id, StartDate = (end ?? seed.Run.EndDate).AddDays(-13), EndDate = end ?? seed.Run.EndDate,
            Fingerprint = Guid.NewGuid().ToString("N"), SchemaVersion = 3, CalculatedAtUtc = clock.Now };
        report = report with { RunId = run.Id, CampaignId = seed.Campaign.Id, Window = new(run.StartDate, run.EndDate),
            CalculatedAtUtc = clock.Now, Cards = [ (card ?? seed.Card) with { EvaluationId = Guid.NewGuid() } ] };
        run.SnapshotJson = JsonSerializer.Serialize(report, ResultsService.Json);
        db.WbRecommendationRuns.Add(run); await db.SaveChangesAsync(); return report;
    }

    private static WbSnapshotDecision Decide(Seed seed, Clock clock, string status, DateOnly? until = null) => new() {
        Id = Guid.NewGuid(), RunId = seed.Run.Id, EvaluationId = seed.Card.EvaluationId, RuleId = seed.Card.Rule.RuleId,
        Status = status, CreatedAtUtc = clock.Now, SnoozeUntil = until, Reason = status == "irrelevant" ? "Не подходит" : null
    };

    [Theory]
    [InlineData("accepted", 1)]
    [InlineData("irrelevant", 0)]
    [InlineData("snoozed", 0)]
    public async Task DecisionsSurviveNewCalculationIds(string status, int visible)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var results = new ResultsService(db, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        db.WbSnapshotDecisions.Add(Decide(seed, clock, status, Detection.AddDays(2))); await db.SaveChangesAsync();
        clock.Now = clock.Now.AddHours(1);
        var report = await NewReportAsync(db, seed, clock);
        var actual = await new RecommendationLifecycle(db, clock).ApplyAsync(report, false, default);
        Assert.Equal(visible, actual.Cards.Count);
        if (visible > 0) Assert.Equal(status, Assert.Single(actual.Cards).Decision!.Status);
        var snapshot = await new RecommendationService(db, Microsoft.Extensions.Options.Options.Create(new RecommendationOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RecommendationService>.Instance, clock)
            .SnapshotAsync(seed.Seller.Id, seed.Campaign.Id, report.RunId!.Value, default);
        Assert.Equal(visible, snapshot!.Cards.Count);
        if (visible > 0) Assert.Equal(status, Assert.Single(snapshot.Cards).Decision!.Status);
        Assert.Single(await db.WbResultRecommendations.ToArrayAsync());
    }

    [Fact]
    public async Task SnoozeReturnsOnMoscowDateWithoutCreatingAnotherOccurrence()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var results = new ResultsService(db, clock);
        var report = await NewReportAsync(db, seed, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(report.RunId!.Value, report.Cards[0].EvaluationId, "R03")], default);
        var decision = Decide(seed, clock, "snoozed", Detection);
        decision.RunId = report.RunId.Value; decision.EvaluationId = report.Cards[0].EvaluationId;
        db.WbSnapshotDecisions.Add(decision); await db.SaveChangesAsync();
        var lifecycle = new RecommendationLifecycle(db, clock);
        clock.Now = At(Detection.AddDays(-1), 20).AddMinutes(59);
        Assert.Empty((await lifecycle.ApplyAsync(report, false, default)).Cards);
        clock.Now = clock.Now.AddMinutes(1); // Midnight Moscow, still the previous UTC date.
        Assert.Null(Assert.Single((await lifecycle.ApplyAsync(report, false, default)).Cards).Decision);
        var snapshot = await new RecommendationService(db, Microsoft.Extensions.Options.Options.Create(new RecommendationOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RecommendationService>.Instance, clock)
            .SnapshotAsync(seed.Seller.Id, seed.Campaign.Id, report.RunId.Value, default);
        Assert.Null(Assert.Single(snapshot!.Cards).Decision);
        var journal = await new ResultsReportReader(db, clock).ReadAsync(seed.Seller.Id, seed.Store.Id, null, Detection, "all", 0, 10, true, default);
        Assert.Equal(0, journal.Funnel.Snoozed);
        Assert.Equal("pending", Assert.Single(journal.Rows).Status);
        Assert.Equal("snoozed", Assert.Single(Assert.Single(journal.Rows).Members).Decision); // Keep the original decision history.
        await results.RegisterAsync(seed.Seller.Id, [new(report.RunId!.Value, report.Cards[0].EvaluationId, "R03")], default);
        Assert.Single(await db.WbResultRecommendations.ToArrayAsync());
    }

    [Fact]
    public async Task RejectedAdviceNeedsBothFourteenDaysAndDoubleEstimateAndRetriesAreIdempotent()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var results = new ResultsService(db, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        db.WbSnapshotDecisions.Add(Decide(seed, clock, "irrelevant")); await db.SaveChangesAsync();
        var doubled = seed.Card with { Presentation = seed.Card.Presentation! with { WeeklySpend = 5600 } };
        var report = await NewReportAsync(db, seed, clock, doubled); var lifecycle = new RecommendationLifecycle(db, clock);
        clock.Now = clock.Now.AddDays(13);
        Assert.Empty((await lifecycle.ApplyAsync(report, false, default)).Cards);
        clock.Now = clock.Now.AddDays(1);
        Assert.Empty((await lifecycle.ApplyAsync(report with { Cards = [seed.Card] }, false, default)).Cards);
        Assert.Empty((await lifecycle.ApplyAsync(report, false, default)).Cards); // An old snapshot cannot reissue advice.
        report = await NewReportAsync(db, seed, clock, doubled);
        Assert.Null(Assert.Single((await lifecycle.ApplyAsync(report, false, default)).Cards).Decision);
        var request = new ResultExposureRequest(report.RunId!.Value, report.Cards[0].EvaluationId, "R03");
        await results.RegisterAsync(seed.Seller.Id, [request], default);
        await results.RegisterAsync(seed.Seller.Id, [request], default);
        Assert.Equal(2, await db.WbResultRecommendations.CountAsync());
        Assert.Equal(2, await db.WbResultExposures.CountAsync());
        var current = Assert.Single((await lifecycle.ApplyAsync(report, false, default)).Cards);
        Assert.Null(current.Decision);
    }

    [Fact]
    public async Task AcceptanceCountSurvivesLaterRejectionButJournalKeepsLatestDecision()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var results = new ResultsService(db, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        db.WbSnapshotDecisions.Add(Decide(seed, clock, "accepted"));
        clock.Now = clock.Now.AddMinutes(1); db.WbSnapshotDecisions.Add(Decide(seed, clock, "accepted"));
        clock.Now = clock.Now.AddMinutes(1); db.WbSnapshotDecisions.Add(Decide(seed, clock, "irrelevant")); await db.SaveChangesAsync();
        var report = await new ResultsReportReader(db, clock).ReadAsync(seed.Seller.Id, seed.Store.Id, null, Detection, "all", 0, 10, true, default);
        Assert.Equal(1, report.Funnel.Accepted); Assert.Equal(1, report.Funnel.Irrelevant);
        Assert.Equal("irrelevant", Assert.Single(Assert.Single(report.Rows).Members).Decision);
    }

    [Fact]
    public async Task LifecycleMigrationPreservesHistoryAndMatchesTheRuntimeModel()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        // Applying generated scripts avoids unnecessary SSH round trips while exercising the actual upgrade.
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(toMigration: "20261028010000_AddBudgetAutoReplenishment"));
        var seed = await SeedAsync(db);
        var rec = new WbResultRecommendation { Id = Guid.NewGuid(), SellerId = seed.Seller.Id, StoreId = seed.Store.Id,
            CampaignId = seed.Campaign.Id, FirstShownAtUtc = DateTime.UtcNow, LastShownAtUtc = DateTime.UtcNow, SubjectKey = "existing" };
        // Use the old schema directly; the new CLR fields intentionally do not exist yet.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO wb_result_recommendations (\"Id\",\"SellerId\",\"StoreId\",\"CampaignId\",\"SubjectKey\",\"FirstShownAtUtc\",\"LastShownAtUtc\",\"CampaignName\",\"Legacy\") VALUES ({rec.Id},{rec.SellerId},{rec.StoreId},{rec.CampaignId},{rec.SubjectKey},{rec.FirstShownAtUtc},{rec.LastShownAtUtc},{"Existing"},{false})");
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript("20261028010000_AddBudgetAutoReplenishment"));
        var restored = await db.WbResultRecommendations.SingleAsync();
        Assert.Equal(rec.Id, restored.Id); Assert.Null(restored.ArchivedAtUtc); Assert.Null(restored.OtherChangeAtUtc);
        Assert.Equal(0, restored.ClearConditionNights); Assert.False(db.Database.HasPendingModelChanges());
    }

    private static async Task<Seed> ReductionSeedAsync(EcomadsDbContext db, Clock clock)
    {
        var seed = await SeedAsync(db);
        var card = seed.Card with { Presentation = seed.Card.Presentation! with { Primary = new("reduce_bid", "Снизьте ставку на 30%", 30) } };
        var report = JsonSerializer.Deserialize<RecommendationResponse>(seed.Run.SnapshotJson, ResultsService.Json)!;
        seed.Run.SnapshotJson = JsonSerializer.Serialize(report with { Cards = [card], CampaignId = seed.Campaign.Id }, ResultsService.Json);
        await db.WbResultObservations.Where(x => x.CampaignId == seed.Campaign.Id).ExecuteDeleteAsync();
        db.WbResultObservations.Add(Settings(seed.Campaign.Id, clock.Now.AddHours(-1), 100, false));
        await db.SaveChangesAsync(); return seed with { Card = card };
    }

    private static WbResultObservation Settings(Guid campaign, DateTime at, decimal bid, bool excluded) => new() {
        Id = Guid.NewGuid(), CampaignId = campaign, AttemptedAtUtc = at, ReceivedAtUtc = at, State = "ready",
        PayloadJson = JsonSerializer.Serialize(new AdvertisingSettingsPayload([new("1", "cluster0", bid)],
            [new("1", excluded ? ["cluster0"] : [])]), ResultsService.Json)
    };

    [Fact]
    public async Task FurtherReductionWaitsForTheMeasurementWindowAndFreshKnownSettings()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await ReductionSeedAsync(db, clock);
        var card = seed.Card with { Rule = seed.Card.Rule with { RuleId = "R06" } };
        var rec = new WbResultRecommendation { Id = Guid.NewGuid(), CampaignId = seed.Campaign.Id, StoreId = seed.Store.Id,
            SellerId = seed.Seller.Id, FirstShownAtUtc = clock.Now, LastShownAtUtc = clock.Now, MeasurementId = Guid.NewGuid() };
        var exposure = new WbResultExposure { Id = Guid.NewGuid(), RecommendationId = rec.Id, RunId = seed.Run.Id,
            EvaluationId = card.EvaluationId, RuleId = "R06", ShownAtUtc = clock.Now, CardJson = JsonSerializer.Serialize(card, ResultsService.Json) };
        var member = new ResultMember(rec.Id, exposure.Id, card,
            new("before", At(Detection.AddDays(-1)), "settings", "ready", Bids: [100]),
            new("after", At(Detection), "settings", "ready", Bids: [80]));
        var measurement = new WbResultMeasurement { Id = rec.MeasurementId.Value, SellerId = seed.Seller.Id, StoreId = seed.Store.Id,
            CampaignId = seed.Campaign.Id, DetectedDate = Detection, Status = "not_evaluated",
            Before = new() { Start = Detection.AddDays(-8), End = Detection.AddDays(-2) },
            After = new() { Start = Detection.AddDays(1), End = Detection.AddDays(7) },
            MembersJson = JsonSerializer.Serialize(new[] { member }, ResultsService.Json) };
        db.WbResultMeasurements.Add(measurement); await db.SaveChangesAsync();
        var history = new RecommendationLifecycle.History(rec, [exposure], []); var lifecycle = new RecommendationLifecycle(db, clock);
        clock.Now = At(Detection.AddDays(6));
        Assert.False(await lifecycle.NewOccurrenceAsync(history, card, Detection.AddDays(4), default, clock.Now));
        clock.Now = At(Detection.AddDays(7));
        db.WbResultObservations.Add(Settings(seed.Campaign.Id, clock.Now, 80, false)); await db.SaveChangesAsync();
        Assert.False(await lifecycle.NewOccurrenceAsync(history, card, Detection.AddDays(5), default, clock.Now));
        clock.Now = At(Detection.AddDays(10));
        Assert.False(await lifecycle.NewOccurrenceAsync(history, card, Detection.AddDays(8), default, clock.Now)); // Stale settings.
        db.WbResultObservations.Add(Settings(seed.Campaign.Id, clock.Now, 80, false)); await db.SaveChangesAsync();
        Assert.True(await lifecycle.NewOccurrenceAsync(history, card, Detection.AddDays(8), default, clock.Now));
        Assert.True(await lifecycle.NewOccurrenceAsync(history, card with { Presentation = card.Presentation! with { Primary = new("exclude", "Исключить") } }, Detection.AddDays(8), default, clock.Now));
        Assert.False(await lifecycle.NewOccurrenceAsync(history, card with { Presentation = card.Presentation! with { Primary = new("watch", "Наблюдать") } }, Detection.AddDays(8), default, clock.Now));
        clock.Now = clock.Now.AddMinutes(1);
        db.WbResultObservations.Add(new() { Id = Guid.NewGuid(), CampaignId = seed.Campaign.Id, AttemptedAtUtc = clock.Now, State = "source_error" });
        await db.SaveChangesAsync();
        Assert.False(await lifecycle.NewOccurrenceAsync(history, card, Detection.AddDays(8), default, clock.Now));
    }

    [Fact]
    public async Task AcceptedActionDoesNotSilentlyChangeWhenLaterCalculationSuggestsASmallerReduction()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await ReductionSeedAsync(db, clock);
        var results = new ResultsService(db, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        db.WbSnapshotDecisions.Add(Decide(seed, clock, "accepted")); await db.SaveChangesAsync();
        clock.Now = clock.Now.AddMinutes(10);
        var smaller = seed.Card with { Presentation = seed.Card.Presentation! with { Primary = new("reduce_bid", "Снизьте ставку на 10%", 10) } };
        var report = await NewReportAsync(db, seed, clock, smaller);
        await results.RegisterAsync(seed.Seller.Id, [new(report.RunId!.Value, report.Cards[0].EvaluationId, "R03")], default);
        clock.Now = clock.Now.AddHours(1); db.WbResultObservations.Add(Settings(seed.Campaign.Id, clock.Now, 90, false)); await db.SaveChangesAsync();
        await results.RunStoreAsync(seed.Store.Id, default);
        Assert.Empty(await db.WbResultMeasurements.ToArrayAsync());
        clock.Now = clock.Now.AddHours(1); db.WbResultObservations.Add(Settings(seed.Campaign.Id, clock.Now, 80, false)); await db.SaveChangesAsync();
        await results.RunStoreAsync(seed.Store.Id, default);
        var measurement = Assert.Single(await db.WbResultMeasurements.ToArrayAsync());
        Assert.Equal(30, Assert.Single(ResultsService.Members(measurement)).Card.Presentation!.Primary.ReductionPercent);
    }

    [Fact]
    public async Task ExclusionInsteadOfReductionClosesAdviceWithoutInventingAppliedActionOrSavings()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await ReductionSeedAsync(db, clock);
        var results = new ResultsService(db, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        db.WbSnapshotDecisions.Add(Decide(seed, clock, "accepted")); await db.SaveChangesAsync();
        clock.Now = clock.Now.AddHours(1); db.WbResultObservations.Add(Settings(seed.Campaign.Id, clock.Now, 100, true)); await db.SaveChangesAsync();
        await results.RunStoreAsync(seed.Store.Id, default);
        Assert.NotNull((await db.WbResultRecommendations.SingleAsync()).OtherChangeAtUtc);
        Assert.Empty(await db.WbResultMeasurements.ToArrayAsync());
        var report = await NewReportAsync(db, seed, clock);
        Assert.Empty((await new RecommendationLifecycle(db, clock).ApplyAsync(report, false, default)).Cards);
        var journal = await new ResultsReportReader(db, clock).ReadAsync(seed.Seller.Id, seed.Store.Id, null, Detection, "all", 0, 10, true, default);
        Assert.Equal(0, journal.Funnel.Applied); Assert.Equal(0, journal.Summary.Savings);
        Assert.Contains("вместо снижения", Assert.Single(journal.Rows).Reason);
        // A later informational exposure must not switch reversion checks from cluster settings to campaign metadata.
        clock.Now = clock.Now.AddMinutes(1);
        var watch = await NewReportAsync(db, seed, clock, seed.Card with { Presentation = seed.Card.Presentation! with { Primary = new("watch", "Наблюдать") } });
        await results.RegisterAsync(seed.Seller.Id, [new(watch.RunId!.Value, watch.Cards[0].EvaluationId, "R03")], default);
        clock.Now = clock.Now.AddDays(4);
        db.WbResultObservations.Add(Settings(seed.Campaign.Id, clock.Now, 100, false)); await db.SaveChangesAsync();
        var returned = await NewReportAsync(db, seed, clock, end: Detection.AddDays(2));
        Assert.Single((await new RecommendationLifecycle(db, clock).ApplyAsync(returned, false, default)).Cards);
    }

    [Fact]
    public async Task ArchiveRequiresThreeConsecutiveConfirmedDaysAndApiRefreshesCannotAccelerateIt()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var results = new ResultsService(db, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        var report = await NewReportAsync(db, seed, clock);
        var quality = new RecommendationQuality(true, true, true, true);
        var evaluation = new ClusterEvaluation(seed.Card.EvaluationId, seed.Card.Subject, seed.Card.Facts, seed.Card.Norm, quality,
            seed.Card.Economy, [seed.Card.Rule with { Status = "not_triggered" }]);
        report = report with { Cards = [], Evaluations = [evaluation] };
        RecommendationResponse Fresh(RecommendationResponse value) => value with { CalculatedAtUtc = clock.Now, Window = new(
            DateOnly.FromDateTime(clock.Now).AddDays(-15), DateOnly.FromDateTime(clock.Now).AddDays(-2)) };
        report = Fresh(report);
        await results.ObserveConditionsAsync(seed.Store.Id, report, default);
        await results.ObserveConditionsAsync(seed.Store.Id, report, default);
        Assert.Equal(1, (await db.WbResultRecommendations.SingleAsync()).ClearConditionNights);
        clock.Now = clock.Now.AddDays(1);
        await results.ObserveConditionsAsync(seed.Store.Id, Fresh(report) with { Evaluations = [evaluation with { Quality = quality with { ClustersComplete = false } }] }, default);
        clock.Now = clock.Now.AddDays(1); await results.ObserveConditionsAsync(seed.Store.Id, Fresh(report), default);
        Assert.Equal(1, (await db.WbResultRecommendations.SingleAsync()).ClearConditionNights);
        clock.Now = clock.Now.AddDays(1); await results.ObserveConditionsAsync(seed.Store.Id, Fresh(report), default);
        Assert.Null((await db.WbResultRecommendations.SingleAsync()).ArchivedAtUtc);
        clock.Now = clock.Now.AddDays(1); await results.ObserveConditionsAsync(seed.Store.Id, Fresh(report), default);
        Assert.NotNull((await db.WbResultRecommendations.SingleAsync()).ArchivedAtUtc);
        var recurrence = await NewReportAsync(db, seed, clock);
        await results.RegisterAsync(seed.Seller.Id, [new(recurrence.RunId!.Value, recurrence.Cards[0].EvaluationId, "R03")], default);
        Assert.Equal(2, await db.WbResultRecommendations.CountAsync());
    }
}

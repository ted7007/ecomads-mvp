using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class ResultsEndpointTests(PostgresFixture postgres)
{
    private static readonly DateOnly Detection = new(2026, 10, 10);
    private static DateTime At(DateOnly day, int hour = 12) => day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public DateTime Now = At(Detection.AddDays(-1), 13); public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed record Seed(Seller Seller, Store Store, Campaign Campaign, Nomenclature Article, WbRecommendationRun Run, RecommendationCard Card);
    private static async Task<Seed> SeedAsync(EcomadsDbContext db, int count = 1, int spread = 0)
    {
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Test", ApiKey = "test", AutoRefreshEnabled = false };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Campaign", WbCampaignId = "123", WbStatus = 9 };
        var article = new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id, WbNomenclatureId = "1", Name = "Product" };
        db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign); db.Nomenclatures.Add(article);
        var cards = Enumerable.Range(0, count).Select(i => new RecommendationCard(Guid.NewGuid(), new(store.Id, campaign.Id, article.Id, "1", "Product", "cluster" + i),
            new("R03", 2, 2, true, "triggered", "spend_without_units", "No units", 300), new(2800, 1000, 150, 0, 0, 0, 1000),
            new(.2m, "store", 1, 0, 0), new(0, null, null, null, null, 200), false,
            new(new("exclude", "Исключите кластер"), [], 0, false, 2800, [], []))).ToArray();
        var run = new WbRecommendationRun { Id = Guid.NewGuid(), SellerId = seller.Id, StoreId = store.Id, CampaignId = campaign.Id,
            StartDate = Detection.AddDays(-15), EndDate = Detection.AddDays(-2), SchemaVersion = 3, CalculatedAtUtc = At(Detection.AddDays(-1)), Fingerprint = Guid.NewGuid().ToString("N") };
        run.SnapshotJson = JsonSerializer.Serialize(new RecommendationResponse(true, true, run.Id, 3, new(run.StartDate, run.EndDate), run.CalculatedAtUtc,
            "preliminary", "", cards, [], [], new(count, count, count, 0)), ResultsService.Json);
        db.WbRecommendationRuns.Add(run);
        for (var day = Detection.AddDays(-10); day <= Detection.AddDays(10); day = day.AddDays(1))
        {
            var at = At(day); var spend = day < Detection ? 2000 : 1900;
            db.CampaignStatistics.Add(new() { CampaignId = campaign.Id, Date = at.Date, Spend = spend, Revenue = 10000 });
            db.CampaignNomenclatureStatistics.Add(new() { CampaignId = campaign.Id, NomenclatureId = article.Id, Date = at.Date,
                Spend = spend, Revenue = 10000, OrderedProducts = 10, Clicks = 100, RecommendationFieldsVerified = true });
            db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = day, Result = "data", Spend = spend, CheckedAtUtc = at });
            TestData.AddConfirmedClusterDay(db, store.Id, campaign.Id, article.Id, day);
            var rawId = Guid.NewGuid();
            db.WbResponseSnapshots.Add(new() { Id = rawId, StoreId = store.Id, RequestedAtUtc = at, ReceivedAtUtc = at, State = "applied", AppliedAtUtc = at, Body = "{}", Sha256 = "test" });
            db.WbProductOrderDays.Add(new() { StoreId = store.Id, Date = day, SnapshotId = rawId, LoadedAtUtc = at, ProductCount = 1 });
            db.WbProductDailyOrders.Add(new() { StoreId = store.Id, Date = day, NmId = 1, OrderCount = 100, OrderSum = 100000 });
            db.WbSourceQualities.Add(new() { StoreId = store.Id, Source = "product_orders", Entity = "", StartDate = day, EndDate = day, State = "data", LastAttemptAtUtc = at, AcceptedAtUtc = at, AcceptedSnapshotId = rawId });
            foreach (var card in cards)
                db.WbClusterStatistics.Add(new() { Id = Guid.NewGuid(), CampaignId = campaign.Id, NomenclatureId = article.Id, Date = day,
                    ClusterName = card.Subject.ClusterName!, Spend = day < Detection ? 400 : 0, OrderedProducts = 0, Clicks = 25 });
            var minus = cards.Where((_, i) => day >= Detection.AddDays(i * spread)).Select(c => c.Subject.ClusterName!).ToArray();
            var metadata = new WbCampaignInfo(123, "Campaign", 9, "cpm", [1], BidType: "unified", ArticleBids: [new(1, 100, 100)]);
            db.WbResultObservations.Add(new() { Id = Guid.NewGuid(), CampaignId = campaign.Id, AttemptedAtUtc = at, ReceivedAtUtc = at, State = "ready",
                PayloadJson = JsonSerializer.Serialize(new AdvertisingSettingsPayload([], [new("1", minus)], metadata, at), ResultsService.Json) });
            void Operational(string source, object payload, string entity = "") => db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id,
                Source = source, EntityKey = entity, State = "ready", RequestedAtUtc = at, ReceivedAtUtc = at, PayloadJson = JsonSerializer.Serialize(payload, ResultsService.Json) });
            Operational("advertising", new[] { metadata }); Operational("budgets", new Dictionary<string, decimal> { ["123"] = 1000 }, "123");
            foreach (var source in new[] { "stocks_fbw", "stocks_fbs" }) Operational(source, new[] { new StockPosition(1, 1, 1, 50) });
        }
        db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed", StartDate = Detection.AddDays(-10),
            EndDate = Detection.AddDays(10), PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
        await db.SaveChangesAsync(); return new(seller, store, campaign, article, run, cards[0]);
    }

    [Fact]
    public async Task ApiMeasuresWithoutAcceptanceKeepsRevisionsAndEnforcesOwnership()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync(); var clock = new Clock();
        using var factory = new Factory(connection, clock); using var client = factory.CreateClient();
        Seed seed;
        using (var scope = factory.Services.CreateScope()) seed = await SeedAsync(scope.ServiceProvider.GetRequiredService<EcomadsDbContext>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/wb/results")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Seller", seed.Seller.Id.ToString());
        var request = new[] { new ResultExposureRequest(seed.Run.Id, seed.Card.EvaluationId, "R03") };
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/wb/recommendations/exposures", request)).StatusCode);
        clock.Now = At(Detection.AddDays(10), 13);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            Assert.Equal(1, await db.WbResultRecommendations.CountAsync()); Assert.Equal(1, await db.WbResultExposures.CountAsync());
            await scope.ServiceProvider.GetRequiredService<ResultsService>().RunStoreAsync(seed.Store.Id, default);
        }
        var url = $"/api/wb/results?storeId={seed.Store.Id}&period=all";
        var report = (await client.GetFromJsonAsync<ResultsReport>(url))!;
        Assert.Equal(700m, report.Summary.Savings); Assert.Equal(1, report.Funnel.AppliedWithoutAcceptance);
        var row = Assert.Single(report.Rows); Assert.Equal("confirmed", row.Status); Assert.Equal(1, row.Revision);
        var detail = (await client.GetFromJsonAsync<ResultDetail>($"/api/wb/results/{row.Id}"))!;
        Assert.Equal(Detection.AddDays(-8), detail.BeforeWindow!.Start); Assert.Equal(Detection.AddDays(-2), detail.BeforeWindow.End);
        Assert.Equal(Detection.AddDays(1), detail.AfterWindow!.Start); Assert.Equal(Detection.AddDays(7), detail.AfterWindow.End);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            var daily = await db.CampaignStatistics.SingleAsync(x => x.CampaignId == seed.Campaign.Id && x.Date == At(Detection.AddDays(1)).Date);
            daily.Spend -= 100; await db.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<ResultsService>(); await service.RunStoreAsync(seed.Store.Id, default); await service.RunStoreAsync(seed.Store.Id, default);
            Assert.Equal(2, await db.WbResultRevisions.CountAsync());
        }
        report = (await client.GetFromJsonAsync<ResultsReport>(url))!; Assert.Equal(800m, report.Summary.Savings);
        detail = (await client.GetFromJsonAsync<ResultDetail>($"/api/wb/results/{row.Id}"))!;
        Assert.Equal(2, detail.Revisions.Count); Assert.Equal(700m, detail.Revisions[1].Evidence.Outcome.Savings);
        var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/results-tests"));
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "fixture.json"), JsonSerializer.Serialize(new { report, detail }, ResultsService.Json));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/wb/results?pageSize=7")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/wb/results?startDate=bad")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Seller"); client.DefaultRequestHeaders.Add("X-Test-Seller", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/wb/results/{row.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/wb/recommendations/exposures", request)).StatusCode);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public async Task GroupingUsesFirstDetectionAndLateActionInterrupts(int spread, int expectedMeasurements)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db, 2, spread);
        var cards = JsonSerializer.Deserialize<RecommendationResponse>(seed.Run.SnapshotJson, ResultsService.Json)!.Cards;
        var service = new ResultsService(db, clock);
        Assert.True(await service.RegisterAsync(seed.Seller.Id, cards.Reverse().Select(c => new ResultExposureRequest(seed.Run.Id, c.EvaluationId, "R03")).ToArray(), default));
        clock.Now = At(Detection.AddDays(10), 13); await service.RunStoreAsync(seed.Store.Id, default);
        Assert.Equal(expectedMeasurements, await db.WbResultMeasurements.CountAsync());
        var first = await db.WbResultMeasurements.OrderBy(x => x.DetectedDate).FirstAsync();
        if (spread == 3) Assert.Equal(2, ResultsService.Members(first).Length);
        else { Assert.Equal("not_evaluated", first.Status); Assert.Contains("Прерван", first.Reason); Assert.Null(first.Savings); }
    }

    [Fact]
    public async Task MissingDataDoesNotBecomeFailureAndRepairCreatesANewRevision()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var service = new ResultsService(db, clock);
        await service.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        clock.Now = At(Detection.AddDays(2)); await service.RunStoreAsync(seed.Store.Id, default);
        var m = await db.WbResultMeasurements.SingleAsync(); Assert.Equal("measuring", m.Status); Assert.Null(m.Savings);
        await db.WbCampaignDailyChecks.Where(x => x.CampaignId == seed.Campaign.Id && x.Date == Detection.AddDays(1)).ExecuteDeleteAsync();
        clock.Now = At(Detection.AddDays(10), 13); await service.RunStoreAsync(seed.Store.Id, default);
        m = await db.WbResultMeasurements.SingleAsync(); Assert.Equal("not_evaluated", m.Status); Assert.Null(m.Savings);
        db.WbCampaignDailyChecks.Add(new() { CampaignId = seed.Campaign.Id, Date = Detection.AddDays(1), Result = "data", Spend = 1900, CheckedAtUtc = clock.Now });
        await db.SaveChangesAsync(); await service.RunStoreAsync(seed.Store.Id, default);
        m = await db.WbResultMeasurements.SingleAsync(); Assert.Equal("confirmed", m.Status); Assert.Equal(2, m.Revision);
        // The database itself protects the displayed evidence, even if a cleanup accidentally tries to remove it.
        var foreignKey = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.WbRecommendationRuns.Where(x => x.Id == seed.Run.Id).ExecuteDeleteAsync());
        Assert.Equal("23503", foreignKey.SqlState);
    }

    [Fact]
    public async Task DisplayedAlternativeCanBeAppliedWithoutInventingR03BidSavings()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var original = JsonSerializer.Deserialize<RecommendationResponse>(seed.Run.SnapshotJson, ResultsService.Json)!;
        var card = seed.Card with { Presentation = seed.Card.Presentation! with { Alternatives = [new("reduce_bid", "Снизьте ставку на 30%", 30)] } };
        seed.Run.SnapshotJson = JsonSerializer.Serialize(original with { Cards = [card] }, ResultsService.Json);
        foreach (var row in await db.WbResultObservations.Where(x => x.CampaignId == seed.Campaign.Id).ToArrayAsync())
        {
            var metadata = new WbCampaignInfo(123, "Campaign", 9, "cpm", [1], BidType: "manual");
            var bid = row.AttemptedAtUtc >= At(Detection.AddDays(1)) ? 81 : row.AttemptedAtUtc >= At(Detection) ? 90 : 100;
            row.PayloadJson = JsonSerializer.Serialize(new AdvertisingSettingsPayload([new("1", "cluster0", bid)], [new("1", [])], metadata, row.AttemptedAtUtc), ResultsService.Json);
        }
        await db.SaveChangesAsync(); var service = new ResultsService(db, clock);
        await service.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, card.EvaluationId, "R03")], default);
        clock.Now = At(Detection.AddDays(2)); await service.RunStoreAsync(seed.Store.Id, default);
        var measurement = await db.WbResultMeasurements.SingleAsync();
        Assert.Equal(Detection.AddDays(1), measurement.DetectedDate);
        Assert.Equal("reduce_bid", Assert.Single(ResultsService.Members(measurement)).Card.Presentation!.Primary.Kind);
        Assert.Equal("not_evaluated", measurement.Status); Assert.Contains("Методика", measurement.Reason); Assert.Null(measurement.Savings);
        Assert.Equal("exclude", JsonSerializer.Deserialize<RecommendationCard>((await db.WbResultExposures.SingleAsync()).CardJson, ResultsService.Json)!.Presentation!.Primary.Kind);
    }

    [Fact]
    public async Task ConcurrentExposureRegistrationIsIdempotent()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        Seed seed;
        await using (var db = postgres.CreateDbContext(connection)) { await db.Database.MigrateAsync(); seed = await SeedAsync(db); }
        var options = new DbContextOptionsBuilder<EcomadsDbContext>().UseNpgsql(connection, o => o.EnableRetryOnFailure()).Options;
        await using var first = new EcomadsDbContext(options); await using var second = new EcomadsDbContext(options);
        var request = new[] { new ResultExposureRequest(seed.Run.Id, seed.Card.EvaluationId, "R03") };
        var results = await Task.WhenAll(new ResultsService(first, new Clock()).RegisterAsync(seed.Seller.Id, request, default),
            new ResultsService(second, new Clock()).RegisterAsync(seed.Seller.Id, request, default));
        Assert.All(results, Assert.True);
        Assert.Equal(1, await first.WbResultRecommendations.CountAsync()); Assert.Equal(1, await first.WbResultExposures.CountAsync());
    }

    [Fact]
    public async Task CampaignPauseStopsForecastAndLateCorrectionsStillReplaceOldTotals()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var service = new ResultsService(db, clock); await service.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        clock.Now = At(Detection.AddDays(10), 13); await service.RunStoreAsync(seed.Store.Id, default);
        var m = await db.WbResultMeasurements.SingleAsync(); Assert.True(m.ActionPreserved);
        clock.Now = At(Detection.AddDays(11));
        db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = seed.Store.Id, Source = "advertising", State = "ready", RequestedAtUtc = clock.Now,
            ReceivedAtUtc = clock.Now, PayloadJson = JsonSerializer.Serialize(new[] { new WbCampaignInfo(123, "Campaign", 11, "cpm", [1]) }, ResultsService.Json) });
        await db.SaveChangesAsync(); await service.RunStoreAsync(seed.Store.Id, default);
        m = await db.WbResultMeasurements.SingleAsync(); Assert.Equal("confirmed", m.Status); Assert.False(m.ActionPreserved);
        Assert.Null(ResultsReportReader.Forecast(m, clock.Now));
        // Corrections are supported beyond the initial 30/45-day operational horizon too.
        clock.Now = At(Detection.AddDays(60));
        var row = await db.CampaignStatistics.SingleAsync(x => x.CampaignId == seed.Campaign.Id && x.Date == At(Detection.AddDays(1)).Date);
        row.Spend -= 100; await db.SaveChangesAsync(); await service.RunStoreAsync(seed.Store.Id, default);
        m = await db.WbResultMeasurements.SingleAsync(); Assert.Equal(800m, m.Savings); Assert.Equal(2, m.Revision);
    }

    [Fact]
    public async Task ExpectedBidRemovalIsNotConfusedWithAForeignChange()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var history = await db.WbResultObservations.OrderBy(x => x.AttemptedAtUtc).ToArrayAsync();
        foreach (var row in history)
        {
            var original = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(row.PayloadJson, ResultsService.Json)!;
            var bids = new List<AdvertisingCluster> { new("1", "other", 200) };
            if (row.AttemptedAtUtc < At(Detection)) bids.Add(new("1", "cluster0", 100));
            row.PayloadJson = JsonSerializer.Serialize(original with { Bids = bids }, ResultsService.Json);
        }
        await db.SaveChangesAsync(); var service = new ResultsService(db, clock);
        await service.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        clock.Now = At(Detection.AddDays(10), 13); await service.RunStoreAsync(seed.Store.Id, default);
        Assert.Equal("confirmed", (await db.WbResultMeasurements.SingleAsync()).Status);
        var changed = await db.WbResultObservations.SingleAsync(x => x.AttemptedAtUtc == At(Detection.AddDays(-5)));
        var data = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(changed.PayloadJson, ResultsService.Json)!;
        changed.PayloadJson = JsonSerializer.Serialize(data with { Bids = data.Bids.Select(b => b.Cluster == "other" ? b with { BidRubles = 300 } : b).ToArray() }, ResultsService.Json);
        await db.SaveChangesAsync(); await service.RunStoreAsync(seed.Store.Id, default);
        var result = await db.WbResultMeasurements.SingleAsync(); Assert.Equal("not_evaluated", result.Status); Assert.Null(result.Savings);
    }

    [Fact]
    public async Task RawEvidenceReferencedByResultsSurvivesJournalRetention()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var service = new ResultsService(db, clock); await service.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        clock.Now = At(Detection.AddDays(10), 13); await service.RunStoreAsync(seed.Store.Id, default);
        var protectedId = Guid.NewGuid(); var obsoleteId = Guid.NewGuid();
        foreach (var id in new[] { protectedId, obsoleteId }) db.WbResponseSnapshots.Add(new() { Id = id, StoreId = seed.Store.Id,
            RequestedAtUtc = clock.Now.AddDays(-101), ReceivedAtUtc = clock.Now.AddDays(-101), State = "applied", Body = "{}", Sha256 = "test" });
        var revision = await db.WbResultRevisions.SingleAsync(); var evidence = JsonSerializer.Deserialize<ResultEvidence>(revision.EvidenceJson, ResultsService.Json)!;
        revision.EvidenceJson = JsonSerializer.Serialize(evidence with { Before = evidence.Before with { Sources = evidence.Before.Sources.Append(protectedId.ToString()).ToArray() } }, ResultsService.Json);
        await db.SaveChangesAsync(); await WbJournalRetentionWorker.PruneAsync(db, clock.Now, default);
        Assert.True(await db.WbResponseSnapshots.AnyAsync(x => x.Id == protectedId)); Assert.False(await db.WbResponseSnapshots.AnyAsync(x => x.Id == obsoleteId));
    }

    [Fact]
    public async Task AReissuedRecommendationAfterReversionStartsANewOccurrence()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var service = new ResultsService(db, clock); await service.RegisterAsync(seed.Seller.Id, [new(seed.Run.Id, seed.Card.EvaluationId, "R03")], default);
        clock.Now = At(Detection.AddDays(10), 13); await service.RunStoreAsync(seed.Store.Id, default);
        db.WbResultObservations.Add(new() { Id = Guid.NewGuid(), CampaignId = seed.Campaign.Id, State = "ready", AttemptedAtUtc = clock.Now, ReceivedAtUtc = clock.Now,
            PayloadJson = JsonSerializer.Serialize(new AdvertisingSettingsPayload([], [new("1", [])]), ResultsService.Json) });
        var response = JsonSerializer.Deserialize<RecommendationResponse>(seed.Run.SnapshotJson, ResultsService.Json)!;
        var next = new WbRecommendationRun { Id = Guid.NewGuid(), SellerId = seed.Seller.Id, StoreId = seed.Store.Id, CampaignId = seed.Campaign.Id,
            StartDate = Detection.AddDays(-5), EndDate = Detection.AddDays(8), Fingerprint = "next", CalculatedAtUtc = clock.Now, SchemaVersion = 3 };
        next.SnapshotJson = JsonSerializer.Serialize(response with { RunId = next.Id, Window = new(next.StartDate, next.EndDate) }, ResultsService.Json);
        db.WbRecommendationRuns.Add(next); await db.SaveChangesAsync();
        clock.Now = clock.Now.AddHours(1); await service.RegisterAsync(seed.Seller.Id, [new(next.Id, seed.Card.EvaluationId, "R03")], default);
        Assert.Equal(2, await db.WbResultRecommendations.CountAsync());
        clock.Now = At(Detection.AddDays(11), 13);
        db.WbResultObservations.Add(new() { Id = Guid.NewGuid(), CampaignId = seed.Campaign.Id, State = "ready", AttemptedAtUtc = clock.Now, ReceivedAtUtc = clock.Now,
            PayloadJson = JsonSerializer.Serialize(new AdvertisingSettingsPayload([], [new("1", ["cluster0"])]), ResultsService.Json) });
        await db.SaveChangesAsync(); await service.RunStoreAsync(seed.Store.Id, default);
        Assert.Equal(2, await db.WbResultMeasurements.CountAsync());
        var report = await new ResultsReportReader(db, clock).ReadAsync(seed.Seller.Id, seed.Store.Id, null, Detection.AddDays(11), "all", 0, 10, true, default);
        Assert.Equal(700m, report.Summary.Savings); Assert.Equal(1, report.Summary.Measuring); Assert.Equal(0m, report.Summary.Forecast);
    }

    [Fact]
    public async Task LegacyDecisionsCannotStartAnAttributedMeasurement()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        db.WbSnapshotDecisions.Add(new() { Id = Guid.NewGuid(), RunId = seed.Run.Id, EvaluationId = seed.Card.EvaluationId, RuleId = "R03", Status = "accepted", CreatedAtUtc = clock.Now.AddDays(-1) });
        await db.SaveChangesAsync(); clock.Now = At(Detection.AddDays(10));
        await new ResultsService(db, clock).RunStoreAsync(seed.Store.Id, default);
        Assert.Empty(await db.WbResultMeasurements.ToArrayAsync());
        var report = await new ResultsReportReader(db, clock).ReadAsync(seed.Seller.Id, seed.Store.Id, null, Detection.AddDays(10), "all", 0, 10, true, default);
        Assert.Equal("historical", Assert.Single(report.Rows).Status); Assert.Equal(0, report.Summary.Savings); Assert.Equal(0, report.Funnel.Issued);
    }

    [Fact]
    public async Task PreSnapshotDecisionsRemainVisibleWithoutInventedExposuresOrEffect()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock(); var seed = await SeedAsync(db);
        var other = TestData.CreateRegularSeller(); db.Sellers.Add(other);
        var otherStore = new Store { Id = Guid.NewGuid(), SellerId = seed.Seller.Id, Name = "Other store" }; db.Stores.Add(otherStore);
        var keys = new[] { $"ctr:{seed.Campaign.Id}", $"drr:{seed.Campaign.Id}", $"cluster:{seed.Campaign.Id}:123:abcd1234" };
        var decisions = keys.Select((key, i) => new WbRecommendationDecision { Id = Guid.NewGuid(), SellerId = seed.Seller.Id,
            RecommendationKey = key, StartDate = Detection.AddDays(-30), EndDate = Detection.AddDays(-1),
            Status = i == 2 ? "snoozed" : "accepted", UpdatedAtUtc = clock.Now.AddDays(-i) }).ToArray();
        db.WbRecommendationDecisions.AddRange(decisions);
        db.WbRecommendationDecisions.Add(new() { Id = Guid.NewGuid(), SellerId = other.Id, RecommendationKey = keys[0],
            StartDate = Detection, EndDate = Detection, Status = "accepted", UpdatedAtUtc = clock.Now });
        await db.SaveChangesAsync();
        var reader = new ResultsReportReader(db, clock);
        var report = await reader.ReadAsync(seed.Seller.Id, seed.Store.Id, null, Detection, "all", 0, 10, true, default);
        Assert.Equal(3, report.Total); Assert.All(report.Rows, row => { Assert.Equal("historical", row.Status); Assert.Null(row.Savings); Assert.Null(row.AppliedDate); });
        Assert.Equal(2, report.Rows.Count(x => x.Members.Single().Decision == "accepted"));
        Assert.Equal(0, report.Funnel.Issued); Assert.Equal(0, report.Funnel.Accepted); Assert.Equal(0, report.Summary.Savings);
        Assert.Empty(await db.WbResultExposures.ToArrayAsync()); Assert.Empty(await db.WbResultMeasurements.ToArrayAsync());
        var rejected = await reader.ReadAsync(seed.Seller.Id, null, null, Detection, "rejected", 0, 10, true, default);
        Assert.Equal("snoozed", Assert.Single(rejected.Rows).Members.Single().Decision);
        Assert.Empty((await reader.ReadAsync(seed.Seller.Id, otherStore.Id, null, Detection, "all", 0, 10, true, default)).Rows);
        Assert.Empty((await reader.ReadAsync(seed.Seller.Id, null, Detection.AddDays(1), Detection.AddDays(2), "all", 0, 10, true, default)).Rows);
        var detail = await reader.DetailAsync(seed.Seller.Id, decisions[0].Id, default); Assert.NotNull(detail); Assert.Empty(detail.Applications);
        Assert.Null(await reader.DetailAsync(other.Id, decisions[0].Id, default));
        Assert.Equal(3, (await reader.ReadAsync(seed.Seller.Id, seed.Store.Id, null, Detection, "all", 0, 10, true, default)).Total);
        Assert.Equal(4, await db.WbRecommendationDecisions.CountAsync());
    }

    private sealed class Factory(string connection, Clock clock) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] = connection, ["Wb:AutoRefresh:Enabled"] = "false", ["Wb:Recommendations:Enabled"] = "false" }));
            builder.ConfigureTestServices(services => {
                services.AddSingleton<TimeProvider>(clock);
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "ResultsTest"; o.DefaultChallengeScheme = "ResultsTest"; })
                    .AddScheme<AuthenticationSchemeOptions, TestAuth>("ResultsTest", _ => { });
            });
        }
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.TryGetValue("X-Test-Seller", out var seller) ? AuthenticateResult.Success(new(new(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, seller.ToString())], Scheme.Name)), Scheme.Name)) : AuthenticateResult.NoResult());
    }
}

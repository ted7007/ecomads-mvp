using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class RecommendationFoundationTests(PostgresFixture postgres)
{
    private static readonly DateOnly Start = new(2026, 9, 27);
    private static readonly RecommendationWindow Window = new(Start, Start.AddDays(13));
    private sealed class FixedClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(Window.EndDate.AddDays(2).ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc)); }
    private sealed record SeedData(Seller Seller, Store Store, Campaign Campaign, Nomenclature Article);

    private static async Task<SeedData> SeedAsync(EcomadsDbContext db)
    {
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Internal", ApiKey = "test" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Campaign", WbCampaignId = "123", WbStatus = 11 };
        var article = new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id, WbNomenclatureId = "1", Name = "Product" };
        db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign); db.Nomenclatures.Add(article);
        for (var i = 0; i < Window.Days; i++)
        {
            var date = Start.AddDays(i);
            TestData.AddConfirmedClusterDay(db, store.Id, campaign.Id, article.Id, date);
            db.CampaignNomenclatureStatistics.Add(new() { CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Revenue = i < 2 ? 10000 : 0, OrderedProducts = i < 2 ? 5 : 0,
                RecommendationFieldsVerified = true, Orders = 999 });
            db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = date, Result = "data", Spend = 1400 });
            db.WbClusterStatistics.AddRange(new() { Id = Guid.NewGuid(), CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date, ClusterName = "no units", Spend = i < 2 ? 400 : 0, OrderedProducts = 0, Orders = 99, Clicks = 5 },
                new() { Id = Guid.NewGuid(), CampaignId = campaign.Id, NomenclatureId = article.Id,
                    Date = date, ClusterName = "high drr", Spend = i < 2 ? 1000 : 0, OrderedProducts = i == 0 ? 1 : i == 1 ? 2 : 0 });
            db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed",
                StartDate = date, EndDate = date, PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
        }
        await db.SaveChangesAsync();
        return new(seller, store, campaign, article);
    }

    private static RecommendationService Service(EcomadsDbContext db, Guid storeId, bool comparable = true) => new(db,
        Options.Create(new RecommendationOptions { SourcesComparable = comparable }),
        NullLogger<RecommendationService>.Instance, new FixedClock());

    private static ControllerContext Context(Guid sellerId) => new() { HttpContext = new DefaultHttpContext
    { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, sellerId.ToString())], "test")) } };

    [Fact]
    public async Task DefaultConfigurationServesEveryConnectedStoreAndKeepsOwnerIsolation()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var first = await SeedAsync(db);
        var second = await SeedAsync(db);
        var service = new RecommendationService(db, Options.Create(new RecommendationOptions()),
            NullLogger<RecommendationService>.Instance, new FixedClock());
        var firstReport = (await service.CalculateAsync(first.Seller.Id, first.Campaign.Id, Window, default))!;
        var secondReport = (await service.CalculateAsync(second.Seller.Id, second.Campaign.Id, Window, default))!;
        Assert.True(firstReport.Enabled);
        Assert.True(secondReport.Enabled);
        Assert.Equal(2, firstReport.Cards.Count);
        Assert.Equal(2, secondReport.Cards.Count);
        Assert.All(secondReport.Cards, card => Assert.Equal(second.Store.Id, card.Subject.StoreId));
        Assert.Null(await service.CalculateAsync(first.Seller.Id, second.Campaign.Id, Window, default));
    }

    [Fact]
    public async Task MissingArticleDayAndSparseClustersExplainGapsWithoutPromisingBackgroundLoading()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        var missingDate = Window.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await db.CampaignNomenclatureStatistics.Where(x => x.CampaignId == seed.Campaign.Id && x.Date == missingDate).ExecuteDeleteAsync();
        await db.WbSourceQualities.Where(x => x.StoreId == seed.Store.Id && x.Source == "clusters" && x.StartDate == Window.EndDate)
            .ExecuteUpdateAsync(x => x.SetProperty(q => q.State, "no_data"));
        var report = (await Service(db, seed.Store.Id).CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.NotEmpty(report.Evaluations);
        Assert.Empty(report.Cards);
        Assert.All(report.Evaluations.Where(x => x.Subject.Scope == "cluster").SelectMany(x => x.Rules).Where(x => x.Status == "skipped"), rule => {
            Assert.Contains("1 из 14 дней", rule.Explanation);
            Assert.Contains("Последний ответ WB", rule.Explanation);
            Assert.DoesNotContain("ожидается фоновая загрузка", rule.Explanation);
        });
        await db.WbSourceQualities.Where(x => x.StoreId == seed.Store.Id && x.Source == "clusters").ExecuteDeleteAsync();
        var legacy = (await Service(db, seed.Store.Id).CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Contains(legacy.Evaluations.Where(x => x.Subject.Scope == "cluster").SelectMany(x => x.Rules), x => x.Explanation.Contains("прежним обработчиком"));
    }

    [Fact]
    public async Task IncompleteLatestClusterSnapshotKeepsRowsButDisablesRecommendations()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        var service = Service(db, seed.Store.Id);
        var before = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.NotEmpty(before.Cards);
        var rowCount = await db.WbClusterStatistics.CountAsync();
        var quality = await db.WbSourceQualities.SingleAsync(x => x.StartDate == Start);
        quality.State = "partial"; quality.LastAttemptAtUtc = DateTime.UtcNow.AddSeconds(1);
        quality.Reason = "WB omitted previously returned clusters.";
        await db.SaveChangesAsync();
        var after = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Empty(after.Cards);
        Assert.All(after.Evaluations.Where(x => x.Subject.Scope == "cluster"), x => Assert.False(x.Quality.ClustersComplete));
        Assert.NotEqual(before.RunId, after.RunId);
        Assert.Equal(rowCount, await db.WbClusterStatistics.CountAsync());
    }

    [Fact]
    public async Task SelectedPeriodSnapshotsAndNormChangesAreReproducibleAndIsolated()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        var service = Service(db, seed.Store.Id);
        var first = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Equal(2, first.Cards.Count);
        Assert.All(first.Cards, x => Assert.True(x.Preliminary));
        Assert.Equal(800m, first.Cards.Single(x => x.Rule.RuleId == "R03").Rule.Threshold);
        Assert.Equal(0, first.Evaluations.Where(x => x.Subject.Scope == "cluster").SelectMany(x => x.Rules).Count(x => x.Status == "skipped"));
        Assert.Equal(first.RunId, (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!.RunId);
        Assert.Equal(1, await db.WbRecommendationRuns.CountAsync());
        var shorter = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, new(Start, Start), default))!;
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(first.Cards), JsonSerializer.SerializeToNode(shorter.Cards))); // Compare the full payload, including deserialized collections.
        Assert.Equal(first.RunId, shorter.RunId);
        Assert.Null(await service.CalculateAsync(Guid.NewGuid(), seed.Campaign.Id, Window, default));
        Assert.Null(await service.SnapshotAsync(Guid.NewGuid(), seed.Campaign.Id, first.RunId!.Value, default));
        var controller = new WbArticleNormsController(db) { ControllerContext = Context(seed.Seller.Id) };
        Assert.IsType<OkObjectResult>(await controller.Put(seed.Campaign.Id, seed.Article.Id, new(40), default));
        var changed = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Empty(changed.Cards);
        Assert.All(changed.Evaluations.Where(x => x.Subject.Scope != "campaign"), x => Assert.Equal("article", x.Norm.Source));
        var snapshot = (await service.SnapshotAsync(seed.Seller.Id, seed.Campaign.Id, first.RunId.Value, default))!;
        Assert.Equal(2, snapshot.Cards.Count);
        Assert.All(snapshot.Evaluations, x => Assert.Equal(0.2m, x.Norm.Value));
        Assert.Equal(first.Rows.Single(x => x.ClusterName == "high drr").EstimatedDrr,
            snapshot.Cards.Single(x => x.Rule.RuleId == "R06").Economy.Drr * 100);
        Assert.IsType<OkObjectResult>(await controller.Put(seed.Campaign.Id, seed.Article.Id, new(null), default));
        var reset = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Equal(2, reset.Cards.Count);
        Assert.All(reset.Evaluations, x => Assert.Equal("default", x.Norm.Source));
        Assert.Equal(2, await db.WbNormRevisions.CountAsync(x => x.NomenclatureId == seed.Article.Id));
    }

    [Fact]
    public async Task MissingCoverageUnitsAndLegacyFieldsAreNotZeroOrHealthy()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        var service = Service(db, seed.Store.Id);
        await db.WbCampaignDailyChecks.Where(x => x.CampaignId == seed.Campaign.Id && x.Date == Start).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        var expanded = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, new(Start, Window.EndDate.AddDays(1)), default))!;
        Assert.Empty(expanded.Cards);
        Assert.Equal("limited", expanded.Status);
        Assert.All(expanded.Evaluations.Where(x => x.Subject.Scope == "cluster").SelectMany(x => x.Rules), x => Assert.Equal("incomplete_data", x.ReasonCode));
        db.WbCampaignDailyChecks.Add(new() { CampaignId = seed.Campaign.Id, Date = Start, Result = "data", Spend = 1400 });
        var basis = await db.CampaignNomenclatureStatistics.FirstAsync();
        basis.RecommendationFieldsVerified = null;
        await db.SaveChangesAsync();
        var legacy = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.All(legacy.Evaluations.Where(x => x.Subject.Scope == "cluster").SelectMany(x => x.Rules), x => Assert.Equal("fields_unverified", x.ReasonCode));
        basis.RecommendationFieldsVerified = true;
        var cluster = await db.WbClusterStatistics.FirstAsync(x => x.ClusterName == "no units");
        cluster.OrderedProducts = null;
        await db.SaveChangesAsync();
        var missing = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Single(missing.Cards);
        Assert.Equal("units_missing", missing.Evaluations.Single(x => x.Subject.ClusterName == "no units").Rules[0].ReasonCode);
        var unverified = (await Service(db, seed.Store.Id, false).CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Empty(unverified.Cards);
        Assert.All(unverified.Evaluations.Where(x => x.Subject.Scope == "cluster").SelectMany(x => x.Rules), x => Assert.Equal("sources_unverified", x.ReasonCode));
    }

    [Fact]
    public async Task ControllerValidatesDatesAndForeignArticleWrites()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        var controller = new WbRecommendationsController(Service(db, seed.Store.Id)) { ControllerContext = Context(seed.Seller.Id) };
        Assert.IsType<OkObjectResult>(await controller.Get(seed.Campaign.Id, default));
        Assert.IsType<NotFoundResult>(await controller.Get(Guid.NewGuid(), default));
        var norms = new WbArticleNormsController(db) { ControllerContext = Context(Guid.NewGuid()) };
        Assert.IsType<NotFoundResult>(await norms.Put(seed.Campaign.Id, seed.Article.Id, new(1), default));
        norms.ControllerContext = Context(seed.Seller.Id);
        Assert.IsType<NotFoundResult>(await norms.Put(seed.Campaign.Id, Guid.NewGuid(), new(1), default));
        Assert.IsType<BadRequestObjectResult>(await norms.Put(seed.Campaign.Id, seed.Article.Id, new(-1), default));
        Assert.IsType<BadRequestObjectResult>(await norms.Put(seed.Campaign.Id, seed.Article.Id, new(20.001m), default));
    }

    [Fact]
    public async Task ConcurrentReadsDoNotDuplicateCalculations()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        async Task<Guid?> Calculate()
        {
            await using var other = postgres.CreateDbContext(db.Database.GetConnectionString()!);
            return (await Service(other, seed.Store.Id).CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!.RunId;
        }
        var ids = await Task.WhenAll(Calculate(), Calculate(), Calculate());
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await db.WbRecommendationRuns.CountAsync());
    }

    [Fact]
    public async Task BackgroundWindowDoesNotReplaceInteractivePeriodsAndRepeatsAreIdempotent()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        seed.Store.AutoRefreshEnabled = false; // This test reads only the prepared data; no WB client.
        await db.SaveChangesAsync();
        var interactive = (await Service(db, seed.Store.Id).CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        var services = new ServiceCollection();
        services.AddScoped(_ => postgres.CreateDbContext(db.Database.GetConnectionString()!));
        services.AddSingleton<IOptions<RecommendationOptions>>(Options.Create(new RecommendationOptions
            { SourcesComparable = true }));
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<RecommendationService>>(NullLogger<RecommendationService>.Instance);
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddScoped<RecommendationService>();
        using var provider = services.BuildServiceProvider();
        var worker = new RecommendationWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<RecommendationOptions>>(), NullLogger<RecommendationWorker>.Instance);
        var now = new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc);
        await worker.RunOnceAsync(now, default);
        await worker.RunOnceAsync(now.AddMinutes(2), default);
        Assert.Equal(1, await db.WbRecommendationRuns.CountAsync());
        Assert.Equal(interactive.RunId, (await Service(db, seed.Store.Id).CalculateAsync(seed.Seller.Id, seed.Campaign.Id, new(Start.AddDays(-30), Start), default))!.RunId);

    }

    [Fact]
    public async Task ImportRecordsCorrectionsWithoutSelectingAStoreAndRespectsGlobalDisable()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        using var input = JsonDocument.Parse("""
        [{"advertId":123,"days":[{"date":"2026-09-27T00:00:00Z","sum":800,"sum_price":10000,
        "views":100,"clicks":10,"atbs":2,"orders":4,"canceled":0,"apps":[{"nms":[
        {"nmId":1,"sum":800,"sum_price":10000,"shks":5,"views":100,"clicks":10,"atbs":2,"orders":4,"canceled":0}]}]}]}]
        """);
        db.ChangeTracker.Clear();
        await new WbFullStatsImporter(db, Options.Create(new RecommendationOptions { Enabled = false }))
            .ImportAsync(seed.Store.Id, input.RootElement, default);
        Assert.Equal(0, await db.WbArticleObservations.CountAsync());
        db.ChangeTracker.Clear();
        var settings = Options.Create(new RecommendationOptions());
        await new WbFullStatsImporter(db, settings).ImportAsync(seed.Store.Id, input.RootElement, default);
        db.ChangeTracker.Clear();
        using var corrected = JsonDocument.Parse(input.RootElement.GetRawText().Replace("\"shks\":5", "\"shks\":6"));
        await new WbFullStatsImporter(db, settings).ImportAsync(seed.Store.Id, corrected.RootElement, default);
        var observations = await db.WbArticleObservations.OrderBy(x => x.ObservedAtUtc).ToListAsync();
        Assert.Equal(2, observations.Count);
        Assert.Equal(5, observations[0].OrderedProducts);
        Assert.Equal(6, observations[1].OrderedProducts);
        Assert.Equal(10000m, observations[0].Revenue);
        Assert.All(observations, x => Assert.Equal(seed.Article.Id, x.NomenclatureId));
    }

    [Fact]
    public async Task DecisionsBelongToOwnedEvidenceRequireDetailsAndSurviveCleanup()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        seed.Store.AutoRefreshEnabled = false;
        await db.SaveChangesAsync();
        var service = Service(db, seed.Store.Id);
        var report = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        var card = report.Cards[0];
        var controller = new WbSnapshotDecisionsController(db, service) { ControllerContext = Context(seed.Seller.Id) };
        WbSnapshotDecisionsController.DecisionRequest Request(string status, DateOnly? date = null, string? reason = null) =>
            new([card.EvaluationId], card.Rule.RuleId, status, date, reason, "test comment");
        Assert.IsType<BadRequestObjectResult>(await controller.Save(seed.Campaign.Id, report.RunId!.Value, Request("snoozed"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Save(seed.Campaign.Id, report.RunId.Value, Request("irrelevant"), default));
        Assert.IsType<NotFoundResult>(await controller.Save(seed.Campaign.Id, report.RunId.Value, Request("accepted") with { EvaluationIds = [Guid.NewGuid()] }, default));
        foreach (var request in new[] { Request("accepted"), Request("snoozed", WbSyncPlanner.Today().AddDays(5)), Request("irrelevant", reason: "Запрос нужен") })
            Assert.IsType<OkObjectResult>(await controller.Save(seed.Campaign.Id, report.RunId.Value, request, default));
        controller.ControllerContext = Context(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(await controller.Save(seed.Campaign.Id, report.RunId.Value, Request("accepted"), default));
        db.WbArticleNorms.Add(new() { CampaignId = seed.Campaign.Id, NomenclatureId = seed.Article.Id, TargetDrr = 40, Version = 2 });
        await db.SaveChangesAsync();
        var changed = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.NotEqual(report.RunId, changed.RunId);
        Assert.All(changed.Cards, c => Assert.Null(c.Decision));
        var snapshot = (await service.SnapshotAsync(seed.Seller.Id, seed.Campaign.Id, report.RunId.Value, default))!;
        Assert.Equal(.2m, snapshot.Cards[0].Norm.Value);
        Assert.Equal(card.Presentation!.Primary, snapshot.Cards[0].Presentation!.Primary);
        Assert.Equal("irrelevant", snapshot.Cards[0].Decision!.Status);
        var row = await db.WbRecommendationRuns.SingleAsync(x => x.Id == report.RunId);
        row.CalculatedAtUtc = DateTime.UtcNow.AddDays(-100);
        await db.SaveChangesAsync();
        // The same predicate used by the worker must retain referenced evidence.
        await db.WbRecommendationRuns.Where(x => x.CalculatedAtUtc < DateTime.UtcNow.AddDays(-90) &&
            !db.WbSnapshotDecisions.Any(d => d.RunId == x.Id)).ExecuteDeleteAsync();
        Assert.True(await db.WbRecommendationRuns.AnyAsync(x => x.Id == report.RunId));
        Assert.Equal(3, await db.WbSnapshotDecisions.CountAsync());
    }

    [Fact]
    public async Task BatchSummaryReusesTheSameCalculationAndHonorsStoreOwnership()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        var foreign = await SeedAsync(db);
        var service = Service(db, seed.Store.Id);
        var report = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        var batch = Assert.Single(await service.CalculateManyAsync(seed.Seller.Id, seed.Store.Id, Window, default));
        Assert.Equal(report.RunId, batch.RunId);
        Assert.Equal(report.Rows, batch.Rows);
        var controller = new WbRecommendationSummaryController(service, db) { ControllerContext = Context(seed.Seller.Id) };
        Assert.IsType<OkObjectResult>(await controller.Get(seed.Store.Id, default));
        Assert.IsType<NotFoundResult>(await controller.Get(foreign.Store.Id, default));
        Assert.Empty(await service.CalculateManyAsync(seed.Seller.Id, foreign.Store.Id, Window, default));
    }

    [Fact]
    public async Task LaunchDateInheritsButArticleOverridesAndHistoricalProtectionHidesCards()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        db.WbCampaignNorms.Add(new() { CampaignId = seed.Campaign.Id, LaunchDate = Start });
        await db.SaveChangesAsync();
        var service = Service(db, seed.Store.Id);
        var protectedReport = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Empty(protectedReport.Cards);
        Assert.All(protectedReport.Evaluations.Where(x => x.Subject.Scope == "cluster"), e => Assert.Contains(e.Presentation!.Restrictions, r => r.Code == "launch"));
        db.WbArticleNorms.Add(new() { CampaignId = seed.Campaign.Id, NomenclatureId = seed.Article.Id, LaunchDate = Start.AddDays(-30) });
        await db.SaveChangesAsync();
        Assert.Equal(2, (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!.Cards.Count);
        var article = await db.WbArticleNorms.SingleAsync();
        article.LaunchDate = null; article.Version++;
        await db.SaveChangesAsync();
        Assert.Empty((await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!.Cards);
    }

    [Fact]
    public async Task PreviousPeriodUsesOwnPriceAndIncompletePreviousOnlyRemovesExplanation()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seed = await SeedAsync(db);
        foreach (var row in await db.WbClusterStatistics.Where(x => x.ClusterName == "high drr").ToListAsync()) row.Orders = 3;
        for (var i = -Window.Days; i < 0; i++)
        {
            var date = Start.AddDays(i);
            TestData.AddConfirmedClusterDay(db, seed.Store.Id, seed.Campaign.Id, seed.Article.Id, date);
            db.CampaignNomenclatureStatistics.Add(new() { CampaignId = seed.Campaign.Id, NomenclatureId = seed.Article.Id,
                Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Revenue = i >= -2 ? 20000 : 0, OrderedProducts = i >= -2 ? 5 : 0, RecommendationFieldsVerified = true });
            db.WbCampaignDailyChecks.Add(new() { CampaignId = seed.Campaign.Id, Date = date, Result = "data", Spend = 1000 });
            db.WbClusterStatistics.Add(new() { Id = Guid.NewGuid(), CampaignId = seed.Campaign.Id, NomenclatureId = seed.Article.Id,
                Date = date, ClusterName = "high drr", Spend = i >= -2 ? 1000 : 0, Orders = 3, OrderedProducts = i == -1 ? 2 : i == -2 ? 1 : 0 });
        }
        db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = seed.Store.Id, Kind = "clusters", Status = "completed",
            StartDate = Start.AddDays(-Window.Days), EndDate = Start.AddDays(-1), PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
        await db.SaveChangesAsync();
        var service = Service(db, seed.Store.Id);
        var report = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        var high = report.Cards.Single(x => x.Rule.RuleId == "R06");
        Assert.Equal(2000m, high.Facts.UnitPrice);
        Assert.Contains(high.Presentation!.Diagnostics, d => d.Code == "D01");
        var check = await db.WbCampaignDailyChecks.SingleAsync(x => x.Date == Start.AddDays(-1));
        db.WbCampaignDailyChecks.Remove(check); await db.SaveChangesAsync();
        var incomplete = (await service.CalculateAsync(seed.Seller.Id, seed.Campaign.Id, Window, default))!;
        Assert.Equal(2, incomplete.Cards.Count);
        Assert.DoesNotContain(incomplete.Cards.Single(x => x.Rule.RuleId == "R06").Presentation!.Diagnostics, d => d.Code == "D01");
    }
}

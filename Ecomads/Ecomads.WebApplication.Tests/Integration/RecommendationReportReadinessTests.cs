using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class RecommendationReportReadinessTests(PostgresFixture postgres)
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Now = new(2026, 10, 30, 7, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [Fact]
    public async Task CompletedBudgetCheckWithoutSearchClustersIsReadyButAnActualSourceErrorIsNot()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var clock = new Clock();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Readiness", ApiKey = "test", AutoRefreshEnabled = false };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Recommendations placement", WbCampaignId = "123", WbStatus = 9 };
        db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign);
        var today = WbSyncPlanner.Today(clock.Now);
        for (var day = today.AddDays(-15); day < today; day = day.AddDays(1))
        {
            db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = day, Result = "data", Spend = 100, CheckedAtUtc = clock.Now });
            db.CampaignStatistics.Add(new() { CampaignId = campaign.Id, Date = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Spend = 100, Revenue = 1000, Orders = 5 });
        }
        db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Source = "advertising", State = "ready",
            RequestedAtUtc = clock.Now, ReceivedAtUtc = clock.Now,
            PayloadJson = JsonSerializer.Serialize(new[] { new WbCampaignInfo(123, "Campaign", 9, "cpm", [], BidType: "manual") }, ResultsService.Json) });
        db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Source = "budgets", EntityKey = "123", State = "ready",
            RequestedAtUtc = clock.Now, ReceivedAtUtc = clock.Now, PayloadJson = "{\"123\":1000}" });
        db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Source = "balance", State = "ready",
            RequestedAtUtc = clock.Now, ReceivedAtUtc = clock.Now, PayloadJson = "{\"amount\":10000}" });
        await db.SaveChangesAsync();
        var service = new RecommendationService(db, Options.Create(new RecommendationOptions()), NullLogger<RecommendationService>.Instance, clock);
        var window = RecommendationPeriods.Economic(clock.Now);
        var report = (await service.CalculateAsync(seller.Id, campaign.Id, window, default))!;
        Assert.Equal("no_deviations", report.Status); Assert.Empty(report.Cards); Assert.Empty(report.Rows);
        Assert.Equal(1, report.Coverage.Evaluated); Assert.Equal(0, report.Coverage.Skipped);
        Assert.Equal("budget_sufficient", Assert.Single(Assert.Single(report.Evaluations).Rules).ReasonCode);
        Assert.Contains("кластерные правила не оценивались", report.Message);
        var cached = (await service.CalculateAsync(seller.Id, campaign.Id, window, default))!;
        Assert.Equal(report.RunId, cached.RunId); Assert.Equal("no_deviations", cached.Status);

        clock.Now = clock.Now.AddMinutes(1);
        db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Source = "budgets", EntityKey = "123", State = "source_error",
            RequestedAtUtc = clock.Now, ReceivedAtUtc = clock.Now, ErrorCode = "http_500" });
        await db.SaveChangesAsync();
        var unavailable = (await service.CalculateAsync(seller.Id, campaign.Id, window, default))!;
        Assert.Equal("not_ready", unavailable.Status); Assert.Empty(unavailable.Cards);
        Assert.Equal(0, unavailable.Coverage.Evaluated); Assert.Equal(1, unavailable.Coverage.Skipped);
        Assert.NotEqual(report.RunId, unavailable.RunId);
    }
}

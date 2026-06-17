using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class KeywordRecommendationOverlayTests
{
    private readonly PostgresFixture _postgres;

    public KeywordRecommendationOverlayTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task GetOverlayAsyncReturnsAllTimeWbKpiAndSelectedPeriodKeywordKpi()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();

        var seller = TestData.CreateActiveDemoSeller();
        var store = new Store
        {
            Id = Guid.NewGuid(),
            Name = "Overlay Store",
            SellerId = seller.Id
        };
        var campaign = new Compaign
        {
            Id = Guid.NewGuid(),
            Name = "Overlay Campaign",
            Number = "SKU-OVERLAY",
            StoreId = store.Id
        };

        dbContext.Sellers.Add(seller);
        dbContext.Stores.Add(store);
        dbContext.Compaigns.Add(campaign);
        dbContext.CompaignStatistics.AddRange(
            new CompaignStatistics
            {
                CompaignId = campaign.Id,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
                Type = CompaignStatisticsType.General,
                Spend = 100,
                Revenue = 500,
                Clicks = 20,
                Ctr = 5,
                Drr = 20
            },
            new CompaignStatistics
            {
                CompaignId = campaign.Id,
                StartDate = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
                Type = CompaignStatisticsType.General,
                Spend = 50,
                Revenue = 100,
                Clicks = 10,
                Ctr = 10,
                Drr = 50
            });
        dbContext.KeywordStatistics.Add(new KeywordStatistics
        {
            Id = Guid.NewGuid(),
            CompaignId = campaign.Id,
            StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
            Phrase = "summer keyword",
            Impressions = 1000,
            Clicks = 50,
            Spend = 30,
            Orders = 3,
            Revenue = 150
        });
        await dbContext.SaveChangesAsync();

        var service = new KeywordRecommendationOverlayService(
            dbContext,
            Options.Create(new RecommendationEngineOptions()),
            NullLogger<KeywordRecommendationOverlayService>.Instance);

        var overlay = await service.GetOverlayAsync(
            campaign.Id,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 7));

        Assert.NotNull(overlay);
        var keyword = Assert.Single(overlay!.Keywords);

        Assert.Equal(150m, keyword.WbKpi.Spend);
        Assert.Equal(600m, keyword.WbKpi.Revenue);
        Assert.Equal(30m, keyword.WbKpi.Clicks);
        Assert.Equal(25m, keyword.WbKpi.Drr);
        Assert.Equal(1000, keyword.PeriodKeywordKpi.Views);
        Assert.Equal(50m, keyword.PeriodKeywordKpi.Clicks);
        Assert.Equal(30m, keyword.PeriodKeywordKpi.Spend);
        Assert.Equal(3, keyword.PeriodKeywordKpi.Orders);
        Assert.Equal(150m, keyword.PeriodKeywordKpi.Revenue);
        Assert.Equal(5m, keyword.PeriodKeywordKpi.Ctr);
        Assert.Equal(20m, keyword.PeriodKeywordKpi.Drr);
    }
}

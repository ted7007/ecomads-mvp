using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbImportTests(PostgresFixture postgres)
{
    [Fact]
    public async Task FullStatsAndClusters_ReplaceSameDayWithoutInventingClusterRevenue()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var storeId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        await using (var db = postgres.CreateDbContext(connection))
        {
            await db.Database.MigrateAsync();
            var seller = TestData.CreateRegularSeller();
            db.Sellers.Add(seller);
            db.Stores.Add(new Store { Id = storeId, SellerId = seller.Id, Name = "WB" });
            db.Campaigns.Add(new Campaign { Id = campaignId, StoreId = storeId,
                WbCampaignId = "35174765", Name = "Campaign" });
            await db.SaveChangesAsync();
        }

        const string fullStats = """
            [{"advertId":35174765,"days":[{"date":"2026-07-01T00:00:00Z","sum":10.25,"sum_price":100,
            "views":100,"clicks":10,"atbs":2,"orders":1,"canceled":0,
            "apps":[{"nms":[{"nmId":123,"name":"Item","sum":10.25,"sum_price":100,
            "views":100,"clicks":10,"atbs":2,"orders":1,"canceled":0}]}]}]}]
            """;
        const string clusters = """
            {"items":[{"advertId":35174765,"nmId":123,"dailyStats":[
            {"date":"2026-07-01","stat":{"normQuery":"cluster","spend":10.25,
            "clicks":10,"orders":1,"atbs":2,"shks":1,"avgPos":3.5,"cpc":1.025}}]}]}
            """;
        var date = new DateOnly(2026, 7, 1);
        var pairs = new[] { new WbNormQueryPair(35174765, 123) };
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await using var db = postgres.CreateDbContext(connection);
            using var fullDocument = JsonDocument.Parse(fullStats);
            await new WbFullStatsImporter(db).ImportAsync(storeId, fullDocument.RootElement, CancellationToken.None);
        }
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await using var db = postgres.CreateDbContext(connection);
            using var clusterDocument = JsonDocument.Parse(clusters);
            await new WbNormQueryImporter(db).ImportAsync(storeId, pairs, date, date,
                clusterDocument.RootElement, CancellationToken.None);
        }

        await using (var db = postgres.CreateDbContext(connection))
        {
            var campaignRows = await db.CampaignStatistics.Where(x => x.CampaignId == campaignId).ToListAsync();
            var articleRows = await db.CampaignNomenclatureStatistics.Where(x => x.CampaignId == campaignId).ToListAsync();
            var clusterRows = await db.WbClusterStatistics.Where(x => x.CampaignId == campaignId).ToListAsync();
            Assert.Single(campaignRows);
            Assert.Single(articleRows);
            var cluster = Assert.Single(clusterRows);
            Assert.Equal(10.25m, cluster.Spend);
            Assert.Null(cluster.Views);
            Assert.Equal(1, cluster.Orders);
        }

        // WB includes pairs without dailyStats when there were no cluster impressions.
        await using (var db = postgres.CreateDbContext(connection))
        {
            using var emptyDocument = JsonDocument.Parse("""
                {"items":[{"advertId":35174765,"nmId":123}]}
                """);
            await new WbNormQueryImporter(db).ImportAsync(storeId, pairs, date, date,
                emptyDocument.RootElement, CancellationToken.None);
        }
        await using (var db = postgres.CreateDbContext(connection))
            Assert.Empty(await db.WbClusterStatistics.Where(x => x.CampaignId == campaignId).ToListAsync());
    }

    [Fact]
    public async Task JamImport_ReplacesSamePeriodAndPreservesMissingMetrics()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var storeId = Guid.NewGuid();
        await using (var db = postgres.CreateDbContext(connection))
        {
            await db.Database.MigrateAsync();
            var seller = TestData.CreateRegularSeller();
            db.Sellers.Add(seller);
            db.Stores.Add(new Store { Id = storeId, SellerId = seller.Id, Name = "WB" });
            db.Nomenclatures.Add(new Nomenclature { Id = Guid.NewGuid(), StoreId = storeId,
                WbNomenclatureId = "123", Name = "Item" });
            await db.SaveChangesAsync();
        }
        const string report = """
            {"data":{"items":[{"nmId":123,"text":"поисковый запрос",
            "frequency":{"current":42},"weekFrequency":140,
            "avgPosition":{"current":5.4},"orders":{"current":3},
            "openCard":{"current":10},"addToCart":{"current":4}}]}}
            """;
        var start = new DateOnly(2026, 7, 1);
        var end = new DateOnly(2026, 7, 7);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await using var db = postgres.CreateDbContext(connection);
            using var document = JsonDocument.Parse(report);
            await new WbJamImporter(db).ImportAsync(storeId, [123], start, end,
                document.RootElement, CancellationToken.None);
        }
        await using (var db = postgres.CreateDbContext(connection))
        {
            var row = Assert.Single(await db.WbJamSearchQueries.Where(x => x.StoreId == storeId).ToListAsync());
            Assert.Equal(42, row.Frequency);
            Assert.Equal(5.4m, row.AveragePosition);
            Assert.Null(row.MedianPosition);
            Assert.Equal(3, row.Orders);
        }
    }
}

using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbImportTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MethodSlotMigration_PreservesFunnelProductPageAndRequestHistory()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var storeId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var requestedAt = DateTime.UtcNow.AddMinutes(-5);
        const string pageState = "{\"Offset\":1000,\"OrderCount\":11,\"OrderSum\":220}";
        const string campaignIds = "[20260701]";
        const string kind = "funnel";
        const string status = "running";
        await using var db = postgres.CreateDbContext(connection);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261001145315_AddWbRefreshRuns");
        var seller = TestData.CreateRegularSeller();
        db.Sellers.Add(seller);
        db.Stores.Add(new Store { Id = storeId, SellerId = seller.Id, Name = "WB" });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO wb_sync_jobs (id, store_id, start_date, end_date, campaign_ids_json,
                pair_ids_json, kind, next_campaign_offset, attempt_count, status, created_at_utc,
                updated_at_utc, next_attempt_at_utc, last_request_at_utc)
            VALUES ({jobId}, {storeId}, {new DateOnly(2026, 7, 1)}, {new DateOnly(2026, 7, 7)},
                {campaignIds}::jsonb, {pageState}::jsonb, {kind}, {1}, {0}, {status},
                {requestedAt}, {requestedAt}, {requestedAt.AddMinutes(30)}, {requestedAt})");
        await migrator.MigrateAsync();
        var job = await db.WbSyncJobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
        Assert.Equal(1, job.NextCampaignOffset);
        using (var restored = JsonDocument.Parse(job.PairIdsJson!))
        {
            Assert.Equal(1000, restored.RootElement.GetProperty("Offset").GetInt32());
            Assert.Equal(11, restored.RootElement.GetProperty("OrderCount").GetInt32());
            Assert.Equal(220m, restored.RootElement.GetProperty("OrderSum").GetDecimal());
        }
        Assert.Equal("running", job.Status);
        var slot = await db.WbMethodSlots.AsNoTracking().SingleAsync(x => x.StoreId == storeId);
        Assert.Equal("funnel_products", slot.Method);
        Assert.InRange(slot.LastRequestAtUtc, requestedAt.AddMilliseconds(-1), requestedAt.AddMilliseconds(1));
    }

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
    public async Task FullStatsNull_KeepsExistingDataAndLeavesRequestedCampaignsUnverified()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var storeId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.MigrateAsync();
        var seller = TestData.CreateRegularSeller();
        db.Sellers.Add(seller);
        db.Stores.Add(new Store { Id = storeId, SellerId = seller.Id, Name = "WB" });
        db.Campaigns.Add(new Campaign { Id = campaignId, StoreId = storeId,
            WbCampaignId = "35174765", Name = "Campaign" });
        db.CampaignStatistics.Add(new CampaignStatistics
        {
            CampaignId = campaignId, Date = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Spend = 12, Revenue = 100
        });
        await db.SaveChangesAsync();

        using var document = JsonDocument.Parse("null");
        var result = await new WbFullStatsImporter(db).ImportAsync(storeId, document.RootElement,
            CancellationToken.None, [35174765], Guid.NewGuid());

        Assert.Equal(0, result.Rows);
        Assert.Equal(new long[] { 35174765L }, result.MissingCampaignIds);
        Assert.Equal(12m, (await db.CampaignStatistics.SingleAsync()).Spend);
        Assert.Empty(await db.WbCampaignDailyChecks.ToListAsync());
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
            var check = Assert.Single(await db.WbJamArticleChecks.Where(x => x.StoreId == storeId).ToListAsync());
            Assert.True(check.HasData);
        }

        await using (var db = postgres.CreateDbContext(connection))
        {
            using var document = JsonDocument.Parse("""{"data":{"items":[]}}""");
            var imported = await new WbJamImporter(db).ImportAsync(storeId, [123], start, end,
                document.RootElement, CancellationToken.None);
            Assert.Equal(0, imported.Rows);
            Assert.Equal(1, imported.WithoutData);
        }
        await using (var db = postgres.CreateDbContext(connection))
        {
            Assert.Empty(await db.WbJamSearchQueries.Where(x => x.StoreId == storeId).ToListAsync());
            var check = Assert.Single(await db.WbJamArticleChecks.Where(x => x.StoreId == storeId).ToListAsync());
            Assert.False(check.HasData);
        }
    }
}

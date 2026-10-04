using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbWorkflowE2ETests(PostgresFixture postgres)
{
    [Fact]
    public async Task SalesFunnelImporter_SumsGroupsFillsEmptyDaysAndReplacesValues()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var seller = TestData.CreateActiveDemoSeller();
        var storeId = Guid.NewGuid();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.MigrateAsync();
        db.Sellers.Add(seller);
        db.Stores.Add(new Store { Id = storeId, SellerId = seller.Id, Marketplace = "Wildberries",
            Name = "WB", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var importer = new WbSalesFunnelImporter(db,
            LoggerFactory.Create(_ => { }).CreateLogger<WbSalesFunnelImporter>());
        using var first = JsonDocument.Parse("""
            {"data":[
              {"currency":"RUB","history":[{"date":"2026-07-01","orderCount":2,"orderSum":100,"openCount":5,"cartCount":3,"buyoutCount":1,"buyoutSum":40}]},
              {"currency":"RUB","history":[{"date":"2026-07-01","orderCount":1,"orderSum":25,"openCount":2,"cartCount":1,"buyoutCount":1,"buyoutSum":20}]}
            ]}
            """);
        await importer.ImportAsync(storeId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3),
            first.RootElement, CancellationToken.None);
        var rows = await db.WbStoreDailyOrders.OrderBy(x => x.Date).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(125m, rows[0].OrderSum);
        Assert.Equal(3, rows[0].OrderCount);
        Assert.Equal(0m, rows[1].OrderSum);
        Assert.Equal(0m, rows[2].OrderSum);

        using var second = JsonDocument.Parse("""
            {"data":[{"currency":"RUB","history":[{"date":"2026-07-01","orderCount":4,"orderSum":170}]}]}
            """);
        await importer.ImportAsync(storeId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3),
            second.RootElement, CancellationToken.None);
        Assert.Equal(170m, (await db.WbStoreDailyOrders.SingleAsync(x => x.Date == new DateOnly(2026, 7, 1))).OrderSum);
        var product = new { statistic = new { selected = new { orderCount = 1, orderSum = 2m },
            past = new { orderCount = 1, orderSum = 3m } } };
        using var pageOne = JsonDocument.Parse(JsonSerializer.Serialize(new
        { data = new { products = Enumerable.Repeat(product, 1000) } }));
        using var pageTwo = JsonDocument.Parse(JsonSerializer.Serialize(new
        { data = new { products = new[] { product } } }));
        var firstPage = WbSalesFunnelImporter.ReadProductsPage(pageOne.RootElement, new WbFunnelPageState());
        Assert.Equal(1000, firstPage.ProductCount);
        Assert.Equal(1000, firstPage.State.Offset);
        var finalPage = WbSalesFunnelImporter.ReadProductsPage(pageTwo.RootElement, firstPage.State);
        Assert.Equal(1001, finalPage.State.OrderCount);
        await importer.ImportProductsAsync(storeId, new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30),
            finalPage.State, CancellationToken.None);
        Assert.Equal(3003m, (await db.WbStoreDailyOrders.SingleAsync(x => x.Date == new DateOnly(2026, 6, 30))).OrderSum);
        Assert.Equal(170m, (await db.WbStoreDailyOrders.SingleAsync(x => x.Date == new DateOnly(2026, 7, 1))).OrderSum);
    }

    [Fact]
    public async Task CostsImporter_ConfirmsZeroDaysAndCoverageIgnoresSilentArchivedCampaigns()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var seller = TestData.CreateActiveDemoSeller();
        var storeId = Guid.NewGuid();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.MigrateAsync();
        db.Sellers.Add(seller);
        db.Stores.Add(new Store { Id = storeId, SellerId = seller.Id, Marketplace = "Wildberries",
            Name = "WB", ApiKey = "test", CreatedAt = DateTime.UtcNow });
        db.Campaigns.Add(new Campaign { Id = Guid.NewGuid(), StoreId = storeId,
            WbCampaignId = "123", Name = "Old campaign", WbStatus = 7,
            WbCreatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            WbDeletedAtUtc = new DateTime(2099, 12, 31, 0, 0, 0, DateTimeKind.Utc) });
        db.WbStoreDailyOrders.AddRange(new[] { 1, 2, 3 }.Select(day => new WbStoreDailyOrders
        { StoreId = storeId, Date = new DateOnly(2026, 7, day), OrderSum = 100m,
            LoadedAtUtc = DateTime.UtcNow }));
        await db.SaveChangesAsync();

        var importer = new WbCostsImporter(db);
        using var response = JsonDocument.Parse("""
            [{"advertId":456,"updTime":"2026-07-01T10:00:00+03:00","updSum":10},
             {"advertId":789,"updTime":"2026-07-01T12:00:00+03:00","updSum":5},
             {"advertId":456,"updTime":"2026-07-03T09:00:00+03:00","updSum":15}]
            """);
        await importer.ImportAsync(storeId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3),
            response.RootElement, CancellationToken.None);
        await importer.ImportAsync(storeId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3),
            response.RootElement, CancellationToken.None);
        var spends = await db.WbStoreDailySpends.OrderBy(x => x.Date).ToListAsync();
        Assert.Equal(new[] { 15m, 0m, 15m }, spends.Select(x => x.Spend));

        var coverage = await new WbDataCoverageService(db).GetAsync(seller.Id,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3), null, CancellationToken.None);
        Assert.Equal("complete", coverage.Status);
        Assert.Equal(10m, coverage.Drr);
        Assert.All(coverage.Days, day => Assert.Equal(0, day.CheckedCampaigns));
    }

    [Fact]
    public async Task CostsImporter_RejectsUndatedCostsWithoutReplacingSavedDays()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var seller = TestData.CreateActiveDemoSeller();
        var storeId = Guid.NewGuid();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.MigrateAsync();
        db.Sellers.Add(seller);
        db.Stores.Add(new Store { Id = storeId, SellerId = seller.Id, Name = "WB",
            CreatedAt = DateTime.UtcNow });
        db.WbStoreDailySpends.Add(new WbStoreDailySpend { StoreId = storeId,
            Date = new DateOnly(2026, 7, 1), Spend = 8m, LoadedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        using var response = JsonDocument.Parse("""[{"updTime":null,"updSum":12}]""");
        await Assert.ThrowsAsync<JsonException>(() => new WbCostsImporter(db).ImportAsync(storeId,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), response.RootElement,
            CancellationToken.None));
        Assert.Equal(8m, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        Assert.Equal(1, await db.WbStoreDailySpends.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SalesFunnelJob_LoadsSevenDaysOrReportsMissingAnalyticsAccess(bool forbidden)
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        using var factory = new WbFactory(connection, forbidden);
        using var client = factory.CreateClient();
        var seller = TestData.CreateActiveDemoSeller();
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.Sellers.Add(seller);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-UserId", seller.Id.ToString());
        using var connected = await client.PostAsJsonAsync("/api/wb/stores/connect", new { token = FakeToken(Guid.NewGuid()) });
        using var connectedJson = JsonDocument.Parse(await connected.Content.ReadAsStringAsync());
        var storeId = connectedJson.RootElement.GetProperty("id").GetGuid();
        using var started = await client.PostAsync($"/api/wb/stores/{storeId}/funnel/sync", null);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        string? status = null;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await Task.Delay(1000);
            using var response = await client.GetAsync($"/api/wb/stores/{storeId}/sync-overview");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var source = body.RootElement.GetProperty("sources").EnumerateArray()
                .Single(x => x.GetProperty("kind").GetString() == "funnel");
            var job = source.GetProperty("lastJob");
            status = job.GetProperty("status").GetString();
            if (status == "failed" || !forbidden && job.GetProperty("processedCount").GetInt32() >= 1)
            {
                if (forbidden) Assert.Equal("wb_403", job.GetProperty("errorCode").GetString());
                break;
            }
        }
        Assert.Equal(forbidden ? "failed" : "running", status);
        await using var check = postgres.CreateDbContext(connection);
        Assert.Equal(forbidden ? 0 : 7, await check.WbStoreDailyOrders.CountAsync(x => x.StoreId == storeId));
    }

    [Fact]
    public async Task Refresh_QueuesAllSourcesAndSurvivesCampaignListRateLimit()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        using var factory = new WbFactory(connection, campaignRefreshRateLimited: true);
        using var client = factory.CreateClient();
        var seller = TestData.CreateActiveDemoSeller();
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.Sellers.Add(seller);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-UserId", seller.Id.ToString());
        using var connected = await client.PostAsJsonAsync("/api/wb/stores/connect", new { token = FakeToken(Guid.NewGuid()) });
        using var connectedJson = JsonDocument.Parse(await connected.Content.ReadAsStringAsync());
        var storeId = connectedJson.RootElement.GetProperty("id").GetGuid();
        await using (var db = postgres.CreateDbContext(connection))
        {
            var store = await db.Stores.SingleAsync(x => x.Id == storeId);
            store.CampaignsRefreshedAtUtc = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        }
        using var refresh = await client.PostAsync($"/api/wb/stores/{storeId}/refresh", null);
        Assert.Equal(HttpStatusCode.Accepted, refresh.StatusCode);
        using var response = JsonDocument.Parse(await refresh.Content.ReadAsStringAsync());
        Assert.False(response.RootElement.GetProperty("campaignsRefreshed").GetBoolean());
        var runId = response.RootElement.GetProperty("runId").GetGuid();
        var jobs = response.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        Assert.Contains(jobs, x => x.GetProperty("kind").GetString() == "fullstats" && x.GetProperty("runId").GetGuid() == runId);
        Assert.Contains(jobs, x => x.GetProperty("kind").GetString() == "funnel_recent" && x.GetProperty("runId").GetGuid() == runId);
        Assert.Contains(jobs, x => x.GetProperty("kind").GetString() == "funnel_backfill" && x.GetProperty("runId").GetGuid() == runId);
        foreach (var kind in new[] { "fullstats", "expenses" })
        {
            var job = Assert.Single(jobs.Where(x => x.GetProperty("kind").GetString() == kind));
            var start = DateOnly.Parse(job.GetProperty("startDate").GetString()!);
            var end = DateOnly.Parse(job.GetProperty("endDate").GetString()!);
            Assert.Equal(30, end.DayNumber - start.DayNumber);
        }
        using var duplicate = await client.PostAsync($"/api/wb/stores/{storeId}/refresh", null);
        Assert.True(duplicate.IsSuccessStatusCode);
        await using (var db = postgres.CreateDbContext(connection))
            Assert.Equal(1, await db.WbSyncJobs.CountAsync(x => x.StoreId == storeId && x.Kind == "fullstats"));
        var followedUp = false;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(1000);
            await using var db = postgres.CreateDbContext(connection);
            followedUp = await db.WbSyncJobs.AnyAsync(x => x.StoreId == storeId && x.Kind == "clusters" && x.RunId == runId) &&
                await db.WbSyncJobs.AnyAsync(x => x.StoreId == storeId && x.Kind == "jam" && x.RunId == runId);
            if (followedUp) break;
        }
        Assert.True(followedUp, "Fullstats completion did not queue clusters and Jam.");
    }

    [Fact]
    public async Task AutoRefresh_RunsOnceAfterSixMoscow()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        using var factory = new WbFactory(connection, autoRefreshEnabled: true);
        using var client = factory.CreateClient();
        var seller = TestData.CreateActiveDemoSeller();
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.Sellers.Add(seller);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-UserId", seller.Id.ToString());
        using var connected = await client.PostAsJsonAsync("/api/wb/stores/connect", new { token = FakeToken(Guid.NewGuid()) });
        using var connectedJson = JsonDocument.Parse(await connected.Content.ReadAsStringAsync());
        var storeId = connectedJson.RootElement.GetProperty("id").GetGuid();
        var services = factory.Services;
        var worker = new WbAutoRefreshWorker(services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<ILogger<WbAutoRefreshWorker>>());
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var now = DateTime.UtcNow;
        if (TimeZoneInfo.ConvertTimeFromUtc(now, moscow).TimeOfDay < TimeSpan.FromHours(6)) now = now.AddDays(-1);
        await worker.RunOnceAsync(now, CancellationToken.None);
        await using (var db = postgres.CreateDbContext(connection))
            Assert.Single(await db.WbSyncJobs.Where(x => x.StoreId == storeId && x.RunId != null)
                .Select(x => x.RunId).Distinct().ToListAsync());
        await worker.RunOnceAsync(now, CancellationToken.None);
        await using (var db = postgres.CreateDbContext(connection))
            Assert.Single(await db.WbSyncJobs.Where(x => x.StoreId == storeId && x.RunId != null)
                .Select(x => x.RunId).Distinct().ToListAsync());
    }

    [Fact]
    public async Task FailedJobRetry_PreservesOriginalAndAllowsOneActiveJobPerKind()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        using var factory = new WbFactory(connection);
        using var client = factory.CreateClient();
        var seller = TestData.CreateActiveDemoSeller();
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.Sellers.Add(seller);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-UserId", seller.Id.ToString());
        using var connected = await client.PostAsJsonAsync("/api/wb/stores/connect", new { token = FakeToken(Guid.NewGuid()) });
        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        using var connectedJson = JsonDocument.Parse(await connected.Content.ReadAsStringAsync());
        var storeId = connectedJson.RootElement.GetProperty("id").GetGuid();
        var failedId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.WbSyncJobs.Add(new WbSyncJob
            {
                Id = failedId, StoreId = storeId, Kind = "fullstats", Status = "failed", Stage = "failed",
                StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 2),
                CampaignIdsJson = "[35174765]", NextCampaignOffset = 0,
                CreatedAtUtc = now.AddMinutes(-5), UpdatedAtUtc = now, CompletedAtUtc = now,
                LastRequestAtUtc = now, NextAttemptAtUtc = now, ErrorCode = "transport_error"
            });
            db.WbMethodSlots.Add(new WbMethodSlot { StoreId = storeId, Method = "fullstats",
                LastRequestAtUtc = now });
            await db.SaveChangesAsync();
        }

        using var retry = await client.PostAsync($"/api/wb/stores/{storeId}/sync-jobs/{failedId}/retry", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        using var retryJson = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal(failedId, retryJson.RootElement.GetProperty("retriedFromJobId").GetGuid());
        Assert.Equal("rate_limit", retryJson.RootElement.GetProperty("waitReason").GetString());
        using var duplicate = await client.PostAsync($"/api/wb/stores/{storeId}/sync-jobs/{failedId}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using (var db = postgres.CreateDbContext(connection))
        {
            var clusterLastRequest = now.AddMinutes(-10);
            db.WbMethodSlots.Add(new WbMethodSlot { StoreId = storeId, Method = "clusters",
                LastRequestAtUtc = clusterLastRequest });
            db.WbSyncJobs.Add(new WbSyncJob
            {
                Id = Guid.NewGuid(), StoreId = storeId, Kind = "clusters", Status = "completed", Stage = "completed",
                StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 2),
                CampaignIdsJson = "[]", PairIdsJson = "[]", CreatedAtUtc = now.AddMinutes(-11),
                UpdatedAtUtc = now.AddMinutes(-9), CompletedAtUtc = now.AddMinutes(-9),
                LastRequestAtUtc = clusterLastRequest, NextAttemptAtUtc = now
            });
            await db.SaveChangesAsync();
            Assert.InRange(await WbRateLimits.NextSlotAsync(db, storeId, "clusters", now, CancellationToken.None),
                now.AddMinutes(19), now.AddMinutes(21));
            db.WbSyncJobs.Add(new WbSyncJob
            {
                Id = Guid.NewGuid(), StoreId = storeId, Kind = "clusters", Status = "pending", Stage = "waiting",
                StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 2),
                CampaignIdsJson = "[]", PairIdsJson = "[]", CreatedAtUtc = now, UpdatedAtUtc = now,
                NextAttemptAtUtc = now.AddMinutes(20)
            });
            await db.SaveChangesAsync();
            Assert.Equal("failed", (await db.WbSyncJobs.SingleAsync(x => x.Id == failedId)).Status);
            Assert.Equal(2, await db.WbSyncJobs.CountAsync(x => x.StoreId == storeId &&
                (x.Status == "pending" || x.Status == "running")));
        }
    }

    [Fact]
    public async Task ConnectSyncAndNorms_WorkThroughHttpWithoutExposingToken()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        using var factory = new WbFactory(connection);
        using var client = factory.CreateClient();
        var seller = TestData.CreateActiveDemoSeller();
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.Sellers.Add(seller);
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Add("X-Test-UserId", seller.Id.ToString());
        var wbToken = FakeToken(Guid.NewGuid());
        using var connected = await client.PostAsJsonAsync("/api/wb/stores/connect", new { token = wbToken });
        var connectedBody = await connected.Content.ReadAsStringAsync();
        Assert.True(connected.IsSuccessStatusCode, connectedBody);
        Assert.DoesNotContain(wbToken, connectedBody);
        using var connectedJson = JsonDocument.Parse(connectedBody);
        var storeId = connectedJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("1234", connectedJson.RootElement.GetProperty("tokenLastFour").GetString());

        await using (var db = postgres.CreateDbContext(connection))
        {
            var stored = await db.Stores.SingleAsync(x => x.Id == storeId);
            Assert.NotEqual(wbToken, stored.ApiKey);
            Assert.DoesNotContain(wbToken, stored.ApiKey!);
        }

        using var norms = await client.PutAsJsonAsync($"/api/wb/norms/stores/{storeId}", new
        {
            targetDrr = 27, minClicks = 25, minSpend = 400, minOrders = 2, deviationPercent = 40, minCtr = 2.5m
        });
        Assert.Equal(HttpStatusCode.OK, norms.StatusCode);
        using var invalidCtr = await client.PutAsJsonAsync($"/api/wb/norms/stores/{storeId}", new
        {
            targetDrr = 27, minClicks = 25, minSpend = 400, minOrders = 2, deviationPercent = 40, minCtr = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCtr.StatusCode);
        using var storedNorms = await client.GetAsync($"/api/wb/norms/stores/{storeId}");
        using var storedNormsJson = JsonDocument.Parse(await storedNorms.Content.ReadAsStringAsync());
        Assert.Equal(2.5m, storedNormsJson.RootElement.GetProperty("values").GetProperty("minCtr").GetDecimal());

        using var sync = await client.PostAsJsonAsync($"/api/wb/stores/{storeId}/sync", new
        {
            startDate = "2026-07-01", endDate = "2026-07-01", campaignIds = new[] { 35174765 }
        });
        Assert.Equal(HttpStatusCode.Accepted, sync.StatusCode);

        var completed = false;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await Task.Delay(1000);
            using var status = await client.GetAsync($"/api/wb/stores/{storeId}/sync");
            using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            var state = json.RootElement.GetProperty("status").GetString();
            Assert.NotEqual("failed", state);
            if (state == "completed") { completed = true; break; }
        }
        Assert.True(completed, "WB worker did not complete the queued fullstats import.");

        using var overview = await client.GetAsync($"/api/wb/stores/{storeId}/sync-overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var overviewJson = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
        var sources = overviewJson.RootElement.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(8, sources.Length);
        var statsSource = Assert.Single(sources.Where(x => x.GetProperty("kind").GetString() == "fullstats"));
        var completedJob = statsSource.GetProperty("lastJob");
        Assert.Equal("completed", completedJob.GetProperty("status").GetString());
        Assert.Equal("campaign", completedJob.GetProperty("unit").GetString());
        Assert.Equal(1, completedJob.GetProperty("processedCount").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, statsSource.GetProperty("lastSuccessAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, sources.Single(x => x.GetProperty("kind").GetString() == "jam")
            .GetProperty("lastSuccessAtUtc").ValueKind);
        var completedJobId = completedJob.GetProperty("id").GetGuid();
        using var history = await client.GetAsync($"/api/wb/stores/{storeId}/sync-jobs?page=1&pageSize=10&kind=fullstats");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var historyJson = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Equal(1, historyJson.RootElement.GetProperty("total").GetInt32());
        using var details = await client.GetAsync($"/api/wb/stores/{storeId}/sync-jobs/{completedJobId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailsJson = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        Assert.Contains(detailsJson.RootElement.GetProperty("events").EnumerateArray(),
            x => x.GetProperty("stage").GetString() == "completed");

        Guid campaignId;
        await using (var db = postgres.CreateDbContext(connection))
        {
            campaignId = await db.Campaigns.Where(x => x.StoreId == storeId).Select(x => x.Id).SingleAsync();
        }
        using var campaignNorms = await client.PutAsJsonAsync($"/api/wb/norms/campaigns/{campaignId}", new
        {
            customName = "Моя кампания", goal = "Проверить рекламу", targetDrr = (int?)null,
            minClicks = (int?)null, minSpend = (int?)null, minOrders = (int?)null,
            deviationPercent = (int?)null, minCtr = 4m
        });
        Assert.Equal(HttpStatusCode.OK, campaignNorms.StatusCode);

        await using (var db = postgres.CreateDbContext(connection))
        {
            using var clusterDocument = JsonDocument.Parse("""
                {"items":[{"advertId":35174765,"nmId":123,"dailyStats":[
                {"date":"2026-07-01","stat":{"normQuery":"пример","spend":10.25,
                "clicks":10,"orders":1,"atbs":2,"shks":1}}]}]}
                """);
            await new WbNormQueryImporter(db).ImportAsync(storeId,
                [new WbNormQueryPair(35174765, 123)], new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1), clusterDocument.RootElement, CancellationToken.None);
            using var jamDocument = JsonDocument.Parse("""
                {"data":{"items":[{"nmId":123,"text":"пример","frequency":{"current":42},
                "avgPosition":{"current":5.4},"openCard":{"current":10},"orders":{"current":2}}]}}
                """);
            await new WbJamImporter(db).ImportAsync(storeId, [123], new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1), jamDocument.RootElement, CancellationToken.None);
        }
        using var clusters = await client.GetAsync($"/api/wb/campaigns/{campaignId}/clusters?startDate=2026-07-01&endDate=2026-07-01");
        Assert.Equal(HttpStatusCode.OK, clusters.StatusCode);
        using var clustersJson = JsonDocument.Parse(await clusters.Content.ReadAsStringAsync());
        Assert.False(clustersJson.RootElement.GetProperty("isPeriodComplete").GetBoolean());
        var cluster = Assert.Single(clustersJson.RootElement.GetProperty("rows").EnumerateArray());
        Assert.Equal("пример", cluster.GetProperty("clusterName").GetString());
        Assert.Equal(JsonValueKind.Null, cluster.GetProperty("views").ValueKind);
        Assert.Equal("Недостаточно данных", cluster.GetProperty("assessment").GetString());

        using var jam = await client.GetAsync($"/api/wb/campaigns/{campaignId}/jam?startDate=2026-07-01&endDate=2026-07-01");
        Assert.Equal(HttpStatusCode.OK, jam.StatusCode);
        using var jamJson = JsonDocument.Parse(await jam.Content.ReadAsStringAsync());
        var query = Assert.Single(jamJson.RootElement.GetProperty("rows").EnumerateArray());
        Assert.Equal("пример", query.GetProperty("searchText").GetString());
        Assert.True(query.GetProperty("matchingLoadedAdCluster").GetBoolean());
        Assert.Equal(42, query.GetProperty("frequency").GetInt64());

        var otherSeller = TestData.CreateActiveDemoSeller();
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.Sellers.Add(otherSeller);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", otherSeller.Id.ToString());
        using var hiddenOverview = await client.GetAsync($"/api/wb/stores/{storeId}/sync-overview");
        Assert.Equal(HttpStatusCode.NotFound, hiddenOverview.StatusCode);
        using var hiddenJob = await client.GetAsync($"/api/wb/stores/{storeId}/sync-jobs/{completedJobId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenJob.StatusCode);
        using var otherSellerJam = await client.GetAsync($"/api/wb/campaigns/{campaignId}/jam?startDate=2026-07-01&endDate=2026-07-01");
        Assert.Equal(HttpStatusCode.NotFound, otherSellerJam.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", seller.Id.ToString());

        using var projects = await client.GetAsync("/api/projects?startDate=2026-07-01&endDate=2026-07-01");
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
        using var projectsJson = JsonDocument.Parse(await projects.Content.ReadAsStringAsync());
        var campaign = Assert.Single(projectsJson.RootElement.EnumerateArray());
        Assert.Equal("Моя кампания", campaign.GetProperty("name").GetString());
        Assert.Equal(27, campaign.GetProperty("targetDrr").GetDecimal());
        Assert.Equal(4m, campaign.GetProperty("minCtr").GetDecimal());
        Assert.Equal(10.25, campaign.GetProperty("kpi").GetProperty("spend").GetDouble(), 2);
        Assert.Equal(10, campaign.GetProperty("kpi").GetProperty("clicks").GetInt32());
        Assert.Equal(100, campaign.GetProperty("kpi").GetProperty("impressions").GetInt32());

        using var daily = await client.GetAsync("/api/statistics/daily?startDate=2026-07-01&endDate=2026-07-03");
        Assert.Equal(HttpStatusCode.OK, daily.StatusCode);
        using var dailyJson = JsonDocument.Parse(await daily.Content.ReadAsStringAsync());
        var days = dailyJson.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, days.Length);
        // Cabinet spend requires a separate verified expenses response.
        Assert.Equal(JsonValueKind.Null, days[0].GetProperty("spend").ValueKind);
        Assert.Equal(1, days[0].GetProperty("loadedCampaigns").GetInt32());
        Assert.Equal(JsonValueKind.Null, days[1].GetProperty("spend").ValueKind);
        Assert.Equal(0, days[1].GetProperty("loadedCampaigns").GetInt32());
        await using (var db = postgres.CreateDbContext(connection))
        {
            db.WbStoreDailyOrders.AddRange(
                new WbStoreDailyOrders { StoreId = storeId, Date = new DateOnly(2026, 7, 1),
                    OrderCount = 2, OrderSum = 200m, Source = "history", LoadedAtUtc = DateTime.UtcNow },
                new WbStoreDailyOrders { StoreId = storeId, Date = new DateOnly(2026, 7, 2),
                    OrderCount = 1, OrderSum = 80m, Source = "history", LoadedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var dailyWithOrders = await client.GetAsync("/api/statistics/daily?startDate=2026-07-01&endDate=2026-07-03");
        using var dailyWithOrdersJson = JsonDocument.Parse(await dailyWithOrders.Content.ReadAsStringAsync());
        var orderDays = dailyWithOrdersJson.RootElement.EnumerateArray().ToArray();
        Assert.Equal(200m, orderDays[0].GetProperty("totalOrderSum").GetDecimal());
        Assert.Equal(2, orderDays[0].GetProperty("totalOrderCount").GetInt32());
        Assert.Equal(80m, orderDays[1].GetProperty("totalOrderSum").GetDecimal());
        Assert.Equal(JsonValueKind.Null, orderDays[2].GetProperty("totalOrderSum").ValueKind);
        using var campaignDaily = await client.GetAsync($"/api/statistics/daily?startDate=2026-07-01&endDate=2026-07-03&campaignId={campaignId}");
        using var campaignDailyJson = JsonDocument.Parse(await campaignDaily.Content.ReadAsStringAsync());
        Assert.Equal(10.25m, campaignDailyJson.RootElement[0].GetProperty("spend").GetDecimal());
        Assert.All(campaignDailyJson.RootElement.EnumerateArray(),
            day => Assert.Equal(JsonValueKind.Null, day.GetProperty("totalOrderSum").ValueKind));

        using var normList = await client.GetAsync($"/api/wb/norms/stores/{storeId}/campaigns");
        Assert.Equal(HttpStatusCode.OK, normList.StatusCode);
        using var normListJson = JsonDocument.Parse(await normList.Content.ReadAsStringAsync());
        var normRow = Assert.Single(normListJson.RootElement.EnumerateArray());
        Assert.Equal("Моя кампания", normRow.GetProperty("name").GetString());
        Assert.Equal(27m, normRow.GetProperty("targetDrr").GetDecimal());
        Assert.True(normRow.GetProperty("isInherited").GetBoolean());

        using var savedDecision = await client.PutAsJsonAsync("/api/wb/recommendation-decisions", new
        {
            recommendationKey = $"drr:{campaignId}", startDate = "2026-07-01", endDate = "2026-07-01",
            status = "accepted"
        });
        Assert.Equal(HttpStatusCode.OK, savedDecision.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", otherSeller.Id.ToString());
        using var hiddenDaily = await client.GetAsync($"/api/statistics/daily?startDate=2026-07-01&endDate=2026-07-01&campaignId={campaignId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDaily.StatusCode);
        using var hiddenDecisions = await client.GetAsync("/api/wb/recommendation-decisions?startDate=2026-07-01&endDate=2026-07-01");
        Assert.Equal(HttpStatusCode.OK, hiddenDecisions.StatusCode);
        using var hiddenDecisionJson = JsonDocument.Parse(await hiddenDecisions.Content.ReadAsStringAsync());
        Assert.Empty(hiddenDecisionJson.RootElement.EnumerateArray());
    }

    private static string FakeToken(Guid sid)
    {
        static string Encode(object value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = new { sid, exp = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
            acc = 1, s = (1L << 6) | (1L << 30) };
        return $"{Encode(new { alg = "none" })}.{Encode(payload)}.1234";
    }

    private sealed class FakeWbClient(bool refreshRateLimited = false) : IWbPromotionClient
    {
        private int campaignListCalls;
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string token, CancellationToken cancellationToken)
        {
            if (refreshRateLimited && Interlocked.Increment(ref campaignListCalls) > 1)
                throw new WbApiException(HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes(30));
            return Task.FromResult<IReadOnlyList<WbCampaignInfo>>([new WbCampaignInfo(35174765, "WB campaign", 9, "cpm", [123])]);
        }

        public Task<JsonDocument> GetFullStatsAsync(string token, IReadOnlyList<long> campaignIds,
            DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            Task.FromResult(JsonDocument.Parse("""
                [{"advertId":35174765,"days":[{"date":"2026-07-01T00:00:00Z","sum":10.25,
                "sum_price":100,"views":100,"clicks":10,"atbs":2,"orders":1,"canceled":0,
                "apps":[{"nms":[{"nmId":123,"name":"Item","sum":10.25,"sum_price":100,
                "views":100,"clicks":10,"atbs":2,"orders":1,"canceled":0}]}]}]}]
                """.Replace("2026-07-01", endDate.ToString("yyyy-MM-dd"))));

        public Task<JsonDocument> GetCostsAsync(string token, DateOnly startDate, DateOnly endDate,
            CancellationToken cancellationToken) => Task.FromResult(JsonDocument.Parse("[]"));

        public Task<JsonDocument> GetNormQueryStatsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs,
            DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            Task.FromResult(JsonDocument.Parse("""{"items":[]}"""));
    }

    private sealed class FakeFunnelClient(bool forbidden) : IWbSalesFunnelClient
    {
        public Task<JsonDocument> GetProductsAsync(string token, DateOnly day, DateOnly pastDay, int offset,
            CancellationToken cancellationToken) => Task.FromResult(JsonDocument.Parse("""{"data":{"products":[]}}"""));
        public Task<JsonDocument> GetGroupedHistoryAsync(string token, DateOnly start, DateOnly end,
            CancellationToken cancellationToken)
        {
            if (forbidden) throw new WbApiException(HttpStatusCode.Forbidden);
            return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                data = new[]
                {
                    new { currency = "RUB", history = new[] { new { date = start.ToString("yyyy-MM-dd"),
                        orderCount = 2, orderSum = 100m, openCount = 4, cartCount = 3, buyoutCount = 1, buyoutSum = 40m } } },
                    new { currency = "RUB", history = new[] { new { date = start.ToString("yyyy-MM-dd"),
                        orderCount = 1, orderSum = 20m, openCount = 2, cartCount = 1, buyoutCount = 1, buyoutSum = 20m } } }
                }
            })));
        }
    }

    private sealed class FakeJamClient : IWbJamClient
    {
        public Task<JsonDocument> GetSearchTextsAsync(string token, IReadOnlyList<long> nmIds,
            DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            Task.FromResult(JsonDocument.Parse("""{"data":{"items":[]}}"""));
    }

    private sealed class WbFactory(string connection, bool funnelForbidden = false,
        bool campaignRefreshRateLimited = false, bool autoRefreshEnabled = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["Wb:AutoRefresh:Enabled"] = autoRefreshEnabled.ToString()
            }));
            builder.ConfigureTestServices(services =>
            {
                if (autoRefreshEnabled) services.RemoveAll<IHostedService>();
                services.RemoveAll<IWbPromotionClient>();
                services.AddSingleton<IWbPromotionClient>(new FakeWbClient(campaignRefreshRateLimited));
                services.RemoveAll<IWbSalesFunnelClient>();
                services.AddSingleton<IWbSalesFunnelClient>(new FakeFunnelClient(funnelForbidden));
                services.RemoveAll<IWbJamClient>();
                services.AddSingleton<IWbJamClient, FakeJamClient>();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-UserId", out var value))
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, value.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

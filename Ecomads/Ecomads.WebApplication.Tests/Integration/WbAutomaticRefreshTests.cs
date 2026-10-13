using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbAutomaticRefreshTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ScheduleCoalescesWhileActiveAndKeepsPausedHistory()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await SeedAsync(db);
        var planner = Planner(db);
        var scheduler = new WbScheduledRefreshService(db, planner);
        var now = DateTime.UtcNow.AddHours(-13);
        await scheduler.RunAsync(store.Id, now, default);
        var count = await db.WbSyncJobs.CountAsync();
        var fresh = await db.WbSyncJobs.SingleAsync(x => x.Kind == "funnel_recent");
        Assert.Equal(WbSyncPlanner.Today(now), fresh.EndDate);
        Assert.Equal(6, fresh.EndDate.DayNumber - fresh.StartDate.DayNumber);
        var year = await db.WbSyncJobs.SingleAsync(x => x.Kind == "funnel_year");
        year.PauseRequestedAtUtc = now;
        await db.SaveChangesAsync();
        await scheduler.RunAsync(store.Id, now.AddHours(13), default);
        Assert.Equal(count, await db.WbSyncJobs.CountAsync());
        Assert.NotNull((await db.WbSyncJobs.SingleAsync(x => x.Id == year.Id)).PauseRequestedAtUtc);
        var schedule = await db.WbRefreshSchedules.SingleAsync(x => x.Task == "funnel_recent");
        Assert.True(schedule.RequestedSlotUtc > schedule.PlannedSlotUtc);
        fresh = await db.WbSyncJobs.SingleAsync(x => x.Kind == "funnel_recent");
        fresh.Status = "completed";
        await db.SaveChangesAsync();
        await scheduler.RunAsync(store.Id, now.AddHours(13), default);
        Assert.Equal(count + 1, await db.WbSyncJobs.CountAsync());
        Assert.Single(await db.WbSyncJobs.Where(x => x.Kind == "funnel_recent" && x.Status == "pending").ToListAsync());
    }

    [Fact]
    public async Task DisabledExpiredAndBlockedStoresDoNotPlan()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await SeedAsync(db);
        var scheduler = new WbScheduledRefreshService(db, Planner(db));
        store.AutoRefreshEnabled = false;
        await db.SaveChangesAsync();
        await scheduler.RunAsync(store.Id, DateTime.UtcNow, default);
        Assert.Empty(await db.WbSyncJobs.ToListAsync());
        store = await db.Stores.SingleAsync();
        store.AutoRefreshEnabled = true;
        store.AutoRefreshBlocked = true;
        await db.SaveChangesAsync();
        await scheduler.RunAsync(store.Id, DateTime.UtcNow, default);
        Assert.Empty(await db.WbSyncJobs.ToListAsync());
        store = await db.Stores.SingleAsync();
        store.AutoRefreshBlocked = false;
        store.TokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await scheduler.RunAsync(store.Id, DateTime.UtcNow, default);
        Assert.Empty(await db.WbSyncJobs.ToListAsync());
    }

    [Fact]
    public async Task MonthRechecksReplaceOldOrdersButBackfillAndOlderSnapshotsDoNot()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await SeedAsync(db);
        var day = WbSyncPlanner.Today().AddDays(-10);
        var importer = new WbSalesFunnelImporter(db, NullLogger<WbSalesFunnelImporter>.Instance);
        db.WbStoreDailyOrders.Add(new WbStoreDailyOrders { StoreId = store.Id, Date = day,
            Source = "history", OrderSum = 100m, LoadedAtUtc = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        await importer.ImportProductsAsync(store.Id, day, day.AddDays(-1), new(OrderSum: 200), default);
        Assert.Equal(100m, (await db.WbStoreDailyOrders.SingleAsync(x => x.Date == day)).OrderSum);
        await importer.ImportProductsAsync(store.Id, day, day.AddDays(-1), new(OrderSum: 200), default,
            replaceExisting: true, snapshotAtUtc: DateTime.UtcNow);
        Assert.Equal(200m, (await db.WbStoreDailyOrders.SingleAsync(x => x.Date == day)).OrderSum);
        await importer.ImportProductsAsync(store.Id, day, day.AddDays(-1), new(OrderSum: 50), default,
            replaceExisting: true, snapshotAtUtc: DateTime.UtcNow.AddHours(-1));
        Assert.Equal(200m, (await db.WbStoreDailyOrders.SingleAsync(x => x.Date == day)).OrderSum);
    }

    [Fact]
    public async Task ReadinessRequiresPostCloseChecksAndIgnoresUnfinishedYear()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await SeedAsync(db);
        var campaign = await db.Campaigns.SingleAsync();
        var date = WbSyncPlanner.Yesterday();
        var close = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified), WbRefreshClock.Moscow);
        db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = date,
            Result = "zero", Spend = 0, CheckedAtUtc = close.AddMinutes(1) });
        db.WbStoreDailySpends.Add(new() { StoreId = store.Id, Date = date, Spend = 0, LoadedAtUtc = close.AddMinutes(1) });
        var orders = new WbStoreDailyOrders { StoreId = store.Id, Date = date, OrderSum = 0, LoadedAtUtc = close.AddMinutes(-1) };
        db.WbStoreDailyOrders.Add(orders);
        db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "funnel_year",
            StartDate = date.AddDays(-364), EndDate = date, CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var readiness = new WbReportReadinessService(db);
        Assert.False((await readiness.GetAsync(store.Id, date, default)).Ready);
        orders.LoadedAtUtc = close.AddMinutes(1);
        await db.SaveChangesAsync();
        Assert.True((await readiness.GetAsync(store.Id, date, default)).Ready);
        var coverage = await new WbDataCoverageService(db).GetAsync(store.SellerId,
            WbSyncPlanner.Today(), WbSyncPlanner.Today(), null, default);
        Assert.NotEqual("complete", coverage.Status);
    }

    [Fact]
    public async Task WorkerUsesFreshSlotBeforeYearAndPreservesYearOffset()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.MigrateAsync();
        var store = await SeedAsync(db);
        var planner = Planner(db);
        var year = (await planner.EnqueueYearFullStatsAsync(store, WbSyncPlanner.Yesterday().AddDays(-364), WbSyncPlanner.Yesterday(), default)).Job!;
        var fresh = (await planner.EnqueueFullStatsAsync(store, WbSyncPlanner.Today(), WbSyncPlanner.Today(), null, default)).Job!;
        var client = new FakePromotionClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<EcomadsDbContext>(options => options.UseNpgsql(connection));
        services.AddSingleton<IWbPromotionClient>(client);
        services.AddSingleton<IWbTokenService, PlainTokens>();
        services.AddScoped<WbFullStatsImporter>();
        using var provider = services.BuildServiceProvider();
        var worker = new WbSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WbSyncWorker>.Instance);
        await worker.ProcessNextAsync(default);
        db.ChangeTracker.Clear();
        Assert.Single(client.RequestedEnds);
        Assert.Equal(WbSyncPlanner.Today(), client.RequestedEnds[0]);
        Assert.Equal("completed", (await db.WbSyncJobs.SingleAsync(x => x.Id == fresh.Id)).Status);
        Assert.Equal(0, (await db.WbSyncJobs.SingleAsync(x => x.Id == year.Id)).NextCampaignOffset);
        await worker.ProcessNextAsync(default);
        Assert.Single(client.RequestedEnds); // Same method slot is still reserved.
    }

    private static WbSyncPlanner Planner(EcomadsDbContext db) =>
        new(db, new FakePromotionClient(), new PlainTokens(), NullLogger<WbSyncPlanner>.Instance);

    [Fact]
    public async Task ConcurrentSchedulersPlanOneJobPerSource()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var firstDb = postgres.CreateDbContext(connection);
        await firstDb.Database.MigrateAsync();
        var store = await SeedAsync(firstDb);
        await using var secondDb = postgres.CreateDbContext(connection);
        var now = DateTime.UtcNow;
        await Task.WhenAll(new WbScheduledRefreshService(firstDb, Planner(firstDb)).RunAsync(store.Id, now, default),
            new WbScheduledRefreshService(secondDb, Planner(secondDb)).RunAsync(store.Id, now, default));
        var kinds = await firstDb.WbSyncJobs.GroupBy(x => x.Kind).Select(x => new { Kind = x.Key, Count = x.Count() }).ToListAsync();
        Assert.Equal(9, kinds.Count);
        Assert.All(kinds, x => Assert.Equal(1, x.Count));
        Assert.Equal(9, await firstDb.WbRefreshSchedules.CountAsync());
    }

    [Fact]
    public async Task YearImportDoesNotOverwritePreviouslyVerifiedExpenseAndAdvertising()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await SeedAsync(db);
        var campaign = await db.Campaigns.SingleAsync();
        var date = WbSyncPlanner.Yesterday();
        db.WbStoreDailySpends.Add(new() { StoreId = store.Id, Date = date, Spend = 15, LoadedAtUtc = DateTime.UtcNow });
        db.CampaignStatistics.Add(new() { CampaignId = campaign.Id, Date = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), Spend = 15 });
        db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = date, Result = "data", Spend = 15, CheckedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        using var costs = JsonDocument.Parse("[]");
        await new WbCostsImporter(db).ImportAsync(store.Id, date, date, costs.RootElement, default, fillMissingOnly: true);
        var client = new FakePromotionClient();
        using var stats = await client.GetFullStatsAsync("test", [123], date, date, default);
        await new WbFullStatsImporter(db).ImportAsync(store.Id, stats.RootElement, default, [123], fillMissingOnly: true);
        Assert.Equal(15m, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        Assert.Equal(15m, (await db.CampaignStatistics.SingleAsync()).Spend);
        Assert.Equal(15m, (await db.WbCampaignDailyChecks.SingleAsync()).Spend);
    }

    private static async Task<Store> SeedAsync(EcomadsDbContext db)
    {
        var seller = TestData.CreateActiveDemoSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB", ApiKey = "test",
            CampaignsRefreshedAtUtc = DateTime.UtcNow, TokenExpiresAtUtc = DateTime.UtcNow.AddDays(30) };
        db.Sellers.Add(seller);
        db.Stores.Add(store);
        db.Campaigns.Add(new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Campaign", WbCampaignId = "123", WbStatus = 9 });
        await db.SaveChangesAsync();
        return store;
    }

    private sealed class PlainTokens : IWbTokenService
    {
        public string Protect(string token) => token;
        public string Unprotect(string token) => token;
        public WbTokenClaims ReadClaims(string token) => throw new NotSupportedException();
    }

    private sealed class FakePromotionClient : IWbPromotionClient
    {
        public List<DateOnly> RequestedEnds { get; } = [];
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string token, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WbCampaignInfo>>([new(123, "Campaign", 9, "cpm", [])]);
        public Task<JsonDocument> GetFullStatsAsync(string token, IReadOnlyList<long> ids, DateOnly start, DateOnly end, CancellationToken ct)
        {
            RequestedEnds.Add(end);
            return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(ids.Select(id => new {
                advertId = id, days = new[] { new { date = $"{end:yyyy-MM-dd}T00:00:00Z", sum = 10, sum_price = 100, views = 100, clicks = 10, atbs = 2, orders = 1, canceled = 0 } }
            }))));
        }
        public Task<JsonDocument> GetCostsAsync(string token, DateOnly start, DateOnly end, CancellationToken ct) =>
            Task.FromResult(JsonDocument.Parse("[]"));
        public Task<JsonDocument> GetNormQueryStatsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs, DateOnly start, DateOnly end, CancellationToken ct) =>
            Task.FromResult(JsonDocument.Parse("{\"items\":[]}"));
    }
}

using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbSparseDataTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SparseDaysPreserveEarlierValuesAndDistinguishCollectionFromConfirmation()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await SeedAsync(db);
        var campaigns = await db.Campaigns.OrderBy(x => x.WbCampaignId).ToArrayAsync();
        var yesterday = WbSyncPlanner.Yesterday();
        var oldDay = yesterday.AddDays(-1);
        var oldCheck = DateTime.UtcNow.AddDays(-1);
        db.WbCampaignDailyChecks.Add(new() { CampaignId = campaigns[0].Id, Date = oldDay,
            Spend = 77, Result = "data", CheckedAtUtc = oldCheck });
        db.CampaignStatistics.Add(new() { CampaignId = campaigns[0].Id,
            Date = DateTime.SpecifyKind(oldDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), Spend = 77 });
        db.WbStoreDailySpends.Add(new() { StoreId = store.Id, Date = yesterday, Spend = 10, LoadedAtUtc = DateTime.UtcNow });
        db.WbStoreDailyOrders.Add(new() { StoreId = store.Id, Date = yesterday, OrderSum = 100, LoadedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        using var response = Stats(yesterday, 123, 10);
        await new WbFullStatsImporter(db).ImportAsync(store.Id, response.RootElement, default, [123, 456],
            requestedStart: oldDay, requestedEnd: yesterday);
        db.ChangeTracker.Clear();
        var checks = await db.WbCampaignDailyChecks.ToArrayAsync();
        Assert.Equal(4, checks.Length);
        var retained = checks.Single(x => x.CampaignId == campaigns[0].Id && x.Date == oldDay);
        Assert.Equal(77m, retained.Spend);
        Assert.Equal("data", retained.Result);
        Assert.Equal("no_data", retained.LastAttemptResult);
        Assert.InRange(retained.CheckedAtUtc, oldCheck.AddMilliseconds(-1), oldCheck.AddMilliseconds(1));
        Assert.Equal(77m, (await db.CampaignStatistics.SingleAsync(x => x.Date ==
            DateTime.SpecifyKind(oldDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc))).Spend);
        Assert.All(checks.Where(x => x.CampaignId == campaigns[1].Id), x =>
        {
            Assert.Null(x.Spend);
            Assert.Equal("unknown", x.Result);
            Assert.Equal("no_data", x.LastAttemptResult);
        });
        var readiness = await new WbReportReadinessService(db).GetAsync(store.Id, yesterday, default);
        Assert.True(readiness.Collected);
        Assert.False(readiness.Ready);
        Assert.Equal("456", Assert.Single(readiness.MissingCampaigns).WbCampaignId);
        Assert.Equal(campaigns[0].Id, Assert.Single(readiness.ConfirmedCampaignIds));

        using var zero = Stats(yesterday, 456, 0);
        await new WbFullStatsImporter(db).ImportAsync(store.Id, zero.RootElement, default, [456],
            requestedStart: yesterday, requestedEnd: yesterday);
        Assert.True((await new WbReportReadinessService(db).GetAsync(store.Id, yesterday, default)).Ready);

        // A later successful sparse response must not pass off the retained value as newly confirmed.
        using var empty = JsonDocument.Parse("[]");
        await new WbFullStatsImporter(db).ImportAsync(store.Id, empty.RootElement, default, [123],
            requestedStart: yesterday, requestedEnd: yesterday);
        readiness = await new WbReportReadinessService(db).GetAsync(store.Id, yesterday, default);
        Assert.True(readiness.Collected);
        Assert.False(readiness.Ready);
        Assert.Equal("123", Assert.Single(readiness.MissingCampaigns).WbCampaignId);
        Assert.Equal(10m, (await db.WbCampaignDailyChecks.SingleAsync(x => x.CampaignId == campaigns[0].Id && x.Date == yesterday)).Spend);
    }

    [Fact]
    public async Task EmptySuccessfulPassDoesNotRetryAndReleasesQueueForMonth()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.MigrateAsync();
        var store = await SeedAsync(db);
        var date = WbSyncPlanner.Yesterday();
        var fresh = Job(store.Id, "fullstats", date);
        var month = Job(store.Id, "fullstats_month", date);
        db.AddRange(fresh, month);
        await db.SaveChangesAsync();
        var client = new Promotion("[]");
        using var provider = Services(connection, client);
        var worker = new WbSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WbSyncWorker>.Instance);
        await worker.ProcessNextAsync(default);
        db.ChangeTracker.Clear();
        var completed = await db.WbSyncJobs.SingleAsync(x => x.Id == fresh.Id);
        Assert.Equal("completed", completed.Status);
        Assert.Equal(2, completed.ItemsWithoutData);
        Assert.Equal(2, WbSyncJobUnits.Total(completed));
        Assert.Single(client.Calls);
        Assert.Equal(2, await db.WbCampaignDailyChecks.CountAsync(x => x.LastAttemptResult == "no_data"));
        await worker.ProcessNextAsync(default);
        Assert.Single(client.Calls); // Shared API interval is still respected.
        await db.WbMethodSlots.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastRequestAtUtc, DateTime.UtcNow.AddHours(-2)));
        await db.WbSyncJobs.Where(x => x.Id == month.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        await worker.ProcessNextAsync(default);
        Assert.Equal(2, client.Calls.Count);
        Assert.Equal("completed", (await db.WbSyncJobs.AsNoTracking().SingleAsync(x => x.Id == month.Id)).Status);
    }

    [Fact]
    public async Task NullResponseIsAnErrorAndCannotMarkSparseDayCollected()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.MigrateAsync();
        var store = await SeedAsync(db);
        var job = Job(store.Id, "fullstats", WbSyncPlanner.Yesterday());
        db.Add(job);
        await db.SaveChangesAsync();
        using var provider = Services(connection, new Promotion("null"));
        await new WbSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WbSyncWorker>.Instance).ProcessNextAsync(default);
        var saved = await db.WbSyncJobs.AsNoTracking().SingleAsync();
        Assert.NotEqual("completed", saved.Status);
        Assert.Equal("invalid_response", saved.ErrorCode);
        Assert.Empty(await db.WbCampaignDailyChecks.ToListAsync());
    }

    private static WbSyncJob Job(Guid storeId, string kind, DateOnly date) => new()
    {
        Id = Guid.NewGuid(), StoreId = storeId, Kind = kind, StartDate = date, EndDate = date,
        CampaignIdsJson = "[123,456]", CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
    };

    private static ServiceProvider Services(string connection, Promotion client)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<EcomadsDbContext>(o => o.UseNpgsql(connection));
        services.AddSingleton<IWbPromotionClient>(client);
        services.AddSingleton<IWbTokenService, Tokens>();
        services.AddScoped<WbFullStatsImporter>();
        return services.BuildServiceProvider();
    }

    private static async Task<Store> SeedAsync(EcomadsDbContext db)
    {
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB", ApiKey = "test",
            CampaignsRefreshedAtUtc = DateTime.UtcNow };
        db.AddRange(seller, store,
            new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Active", WbCampaignId = "123", WbStatus = 9 },
            new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Paused", WbCampaignId = "456", WbStatus = 11 });
        await db.SaveChangesAsync();
        return store;
    }

    private static JsonDocument Stats(DateOnly date, long id, decimal spend) => JsonDocument.Parse(JsonSerializer.Serialize(new[]
    {
        new { advertId = id, days = new[] { new { date = $"{date:yyyy-MM-dd}T00:00:00Z", sum = spend,
            sum_price = 100, views = 100, clicks = 10, atbs = 2, orders = 1, canceled = 0 } } }
    }));

    private sealed class Promotion(string response) : IWbPromotionClient
    {
        public List<DateOnly> Calls { get; } = [];
        public Task<JsonDocument> GetFullStatsAsync(string token, IReadOnlyList<long> ids, DateOnly start, DateOnly end, CancellationToken ct)
        { Calls.Add(end); return Task.FromResult(JsonDocument.Parse(response)); }
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetCostsAsync(string token, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetNormQueryStatsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Tokens : IWbTokenService
    {
        public string Protect(string token) => token;
        public string Unprotect(string token) => token;
        public WbTokenClaims ReadClaims(string token) => throw new NotSupportedException();
    }
}

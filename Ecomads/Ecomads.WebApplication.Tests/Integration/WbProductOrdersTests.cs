using System.Security.Claims;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbProductOrdersTests(PostgresFixture postgres)
{
    private static DateOnly Day => WbSyncPlanner.Yesterday();
    private static async Task<(Seller Seller, Store Store)> Seed(EcomadsDbContext db)
    {
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "test", ApiKey = "test", AutoRefreshEnabled = true,
            CampaignsRefreshedAtUtc = DateTime.UtcNow };
        db.Sellers.Add(seller); db.Stores.Add(store); await db.SaveChangesAsync(); return (seller, store);
    }
    private static WbResponseSnapshot Snapshot(Store store, IEnumerable<long> products, decimal sum = 30000,
        WbFunnelPageState? state = null, DateTime? at = null, Guid? jobId = null)
    {
        var job = new WbSyncJob { Id = jobId ?? Guid.NewGuid(), StoreId = store.Id, Kind = "funnel_month", StartDate = Day.AddDays(-1), EndDate = Day,
            CampaignIdsJson = JsonSerializer.Serialize(new[] { long.Parse(Day.AddDays(-1).ToString("yyyyMMdd")) }), PairIdsJson = state == null ? null : JsonSerializer.Serialize(state) };
        var body = JsonSerializer.Serialize(new { data = new { products = products.Select(id => new { product = new { nmId = id, currency = "RUB" },
            statistic = new { selected = new { orderSum = sum, orderCount = 3 }, past = new { orderSum = sum / 2, orderCount = 2 } } }) } });
        return new() { Id = Guid.NewGuid(), JobId = job.Id, StoreId = store.Id, Kind = job.Kind, JobStateJson = JsonSerializer.Serialize(job),
            RequestedAtUtc = at ?? DateTime.UtcNow, ReceivedAtUtc = at ?? DateTime.UtcNow, HttpStatus = 200, State = "applied", AppliedAtUtc = DateTime.UtcNow,
            Body = body, Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))) };
    }
    private static async Task Apply(EcomadsDbContext db, WbResponseSnapshot snapshot)
    {
        db.WbResponseSnapshots.Add(snapshot); await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await new WbProductOrdersImporter(db).ApplyAsync(snapshot, default); await tx.CommitAsync();
    }
    private static Campaign Campaign(Store store, string id, int status = 9) => new() { Id = Guid.NewGuid(), StoreId = store.Id,
        Name = id, WbCampaignId = id, WbStatus = status };
    private static void Advertising(EcomadsDbContext db, Store store, Campaign campaign, Nomenclature article, decimal spend)
    {
        db.CampaignNomenclatureStatistics.Add(new() { CampaignId = campaign.Id, NomenclatureId = article.Id,
            Date = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Spend = spend, Revenue = 10000, RecommendationFieldsVerified = true });
        db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = Day, Result = "data", Spend = spend, CheckedAtUtc = DateTime.UtcNow });
        foreach (var entity in new[] { campaign.Id.ToString(), $"{campaign.Id}:{article.Id}" })
            db.WbSourceQualities.Add(new() { StoreId = store.Id, Source = "fullstats", Entity = entity, StartDate = Day, EndDate = Day,
                State = "data", LastAttemptAtUtc = DateTime.UtcNow });
    }

    [Fact]
    public async Task EndpointUsesAllCampaignsOfSameStoreIncludingArchiveAndKeepsAdvertisingFields()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var (seller, store) = await Seed(db); var (_, otherStore) = await Seed(db);
        var first = Campaign(store, "1"); var second = Campaign(store, "2", 7); var other = Campaign(otherStore, "3");
        var article = new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id, WbNomenclatureId = "42", Name = "Product" };
        var otherArticle = new Nomenclature { Id = Guid.NewGuid(), StoreId = otherStore.Id, WbNomenclatureId = "42", Name = "Other" };
        db.Campaigns.AddRange(first, second, other); db.Nomenclatures.AddRange(article, otherArticle);
        Advertising(db, store, first, article, 1000); Advertising(db, store, second, article, 2000); Advertising(db, otherStore, other, otherArticle, 99999);
        await db.SaveChangesAsync(); await Apply(db, Snapshot(store, [42]));
        var controller = new StatisticsController(db) { ControllerContext = new() { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, seller.Id.ToString())], "test")) } } };
        var rows = Assert.IsType<List<NomenclatureStatisticsDto>>(Assert.IsType<OkObjectResult>(await controller.GetNomenclatures(first.Id, Day, Day, default)).Value);
        var row = Assert.Single(rows);
        Assert.Equal(1000m, row.Spend); Assert.Equal(10000m, row.Revenue);
        Assert.Equal(30000m, row.TotalOrderSum); Assert.Equal(3000m, row.TotalAdvertisingSpend); Assert.Equal(10m, row.TotalDrr);
        Assert.Equal("complete", row.TotalStatus); Assert.Equal(1, row.TotalOrderDays);
        var longer = (await new WbProductTotals(db).GetAsync(store.Id, ["42"], Day.AddDays(-2), Day, default))["42"];
        Assert.Null(longer.OrderSum); Assert.Null(longer.Drr); Assert.Equal(2, longer.OrderDays); Assert.Equal(3, longer.ExpectedDays);
        Assert.IsType<NotFoundResult>(await controller.GetNomenclatures(other.Id, Day, Day, default));
        Assert.IsType<BadRequestObjectResult>(await controller.GetNomenclatures(first.Id, Day, Day.AddDays(-1), default));
        // Any missing campaign day withholds the total DRR, while known product orders remain visible.
        db.WbCampaignDailyChecks.Remove(await db.WbCampaignDailyChecks.SingleAsync(x => x.CampaignId == second.Id)); await db.SaveChangesAsync();
        var incomplete = (await new WbProductTotals(db).GetAsync(store.Id, ["42"], Day, Day, default))["42"];
        Assert.Equal(30000m, incomplete.OrderSum); Assert.Null(incomplete.Drr); Assert.Null(incomplete.AdvertisingSpend);
        Assert.Equal("preliminary", incomplete.Status);
    }

    [Fact]
    public async Task PaginationPublishesOnlyCompleteChainAndSurvivesRepeatedProcessing()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (_, store) = await Seed(db);
        var first = Snapshot(store, Enumerable.Range(1, 1000).Select(x => (long)x)); await Apply(db, first);
        Assert.Empty(await db.WbProductDailyOrders.ToArrayAsync());
        var state = new WbFunnelPageState(Offset: 1000, SeenProductIds: Enumerable.Range(1,1000).Select(x => (long)x).ToArray(), SnapshotIds: [first.Id]);
        var last = Snapshot(store, [1001], state: state, jobId: first.JobId); await Apply(db, last);
        Assert.Equal(2002, await db.WbProductDailyOrders.CountAsync());
        Assert.All(await db.WbProductOrderDays.ToArrayAsync(), x => Assert.Equal(1001, x.ProductCount));
        await new WbProductOrdersImporter(db).ApplyAsync(last, default);
        Assert.Equal(2002, await db.WbProductDailyOrders.CountAsync());
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("checksum")]
    [InlineData("missing-page")]
    public async Task BrokenChainNeverPublishesPartialOrInflatedOrders(string failure)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (_, store) = await Seed(db);
        var first = Snapshot(store, Enumerable.Range(1, 1000).Select(x => (long)x)); await Apply(db, first);
        var state = new WbFunnelPageState(Offset: 1000, SnapshotIds: [failure == "missing-page" ? Guid.NewGuid() : first.Id]);
        var last = Snapshot(store, [failure == "overlap" ? 1 : 1001], state: state, jobId: first.JobId);
        if (failure == "checksum") last.Sha256 = "wrong";
        await Apply(db, last);
        Assert.Empty(await db.WbProductDailyOrders.ToArrayAsync()); Assert.Equal("invalid_product_snapshot", last.ProductOrdersError);
    }

    [Fact]
    public async Task NewerSnapshotReplacesWholeDayOlderReplayCannotRestoreRemovedProductsAndZeroStaysKnown()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (_, store) = await Seed(db);
        var old = Snapshot(store, [42,43], at: DateTime.UtcNow.AddHours(-2)); await Apply(db, old);
        db.ChangeTracker.Clear();
        var newer = Snapshot(store, [42], sum: 0); await Apply(db, newer);
        Assert.Equal(2, await db.WbProductDailyOrders.CountAsync());
        Assert.All(await db.WbProductDailyOrders.ToArrayAsync(), x => Assert.Equal(0m, x.OrderSum));
        db.ChangeTracker.Clear();
        var lateOld = Snapshot(store, [42,43], at: DateTime.UtcNow.AddHours(-1)); await Apply(db, lateOld);
        Assert.Equal(2, await db.WbProductDailyOrders.CountAsync());
        var totals = await new WbProductTotals(db).GetAsync(store.Id, ["42", "43"], Day, Day, default);
        Assert.Equal(0m, totals["42"].OrderSum); Assert.Null(totals["42"].Drr);
        Assert.Null(totals["43"].OrderSum); // Absent product is not automatically zero.
    }

    [Fact]
    public async Task RecoveryReusesRawSnapshotsWithoutChangingJobsOrStoreTotalsThenPlansOnlyThirtyDaysOnce()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (_, store) = await Seed(db);
        db.WbStoreDailyOrders.Add(new() { StoreId = store.Id, Date = Day, OrderSum = 123, Source = "history", LoadedAtUtc = DateTime.UtcNow });
        var snapshot = Snapshot(store, [42]); db.WbResponseSnapshots.Add(snapshot); await db.SaveChangesAsync();
        var planner = new WbSyncPlanner(db, null!, null!, NullLogger<WbSyncPlanner>.Instance);
        var refresh = new WbProductOrdersRefresh(db, planner, TimeProvider.System);
        await refresh.RunAsync(store, default);
        Assert.Equal(2, await db.WbProductDailyOrders.CountAsync()); Assert.Empty(await db.WbSyncJobs.ToArrayAsync());
        Assert.Equal(123m, (await db.WbStoreDailyOrders.SingleAsync()).OrderSum);
        await refresh.RunAsync(store, default); await refresh.RunAsync(store, default);
        var job = Assert.Single(await db.WbSyncJobs.ToArrayAsync()); Assert.Equal("product_orders", job.Kind);
        Assert.Equal(Day.AddDays(-29), job.StartDate); Assert.Equal(Day, job.EndDate);
        Assert.DoesNotContain(await db.WbSyncJobs.ToArrayAsync(), x => x.Kind == "funnel_year");
    }

    [Fact]
    public async Task NoProgressPausesOnlyProductJobAndResumeDoesNotImmediatelyPauseAgain()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (seller, store) = await Seed(db);
        var job = new WbSyncJob { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "product_orders", Status = "pending",
            CampaignIdsJson = "[]", CreatedAtUtc = DateTime.UtcNow.AddHours(-25), UpdatedAtUtc = DateTime.UtcNow,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1) };
        db.WbSyncJobs.Add(job); await db.SaveChangesAsync();
        var services = new ServiceCollection().AddDbContext<EcomadsDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString())).BuildServiceProvider();
        var worker = new WbSyncWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<WbSyncWorker>.Instance);
        await worker.ProcessNextAsync(default); db.ChangeTracker.Clear();
        var paused = await db.WbSyncJobs.SingleAsync(); Assert.NotNull(paused.PauseRequestedAtUtc); Assert.Equal("no_progress", paused.ErrorCode);
        var controller = new WbSyncVisibilityController(db) { ControllerContext = new() { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, seller.Id.ToString())], "test")) } } };
        Assert.IsType<OkObjectResult>(await controller.Resume(store.Id, job.Id, default));
        Assert.Null(paused.PauseRequestedAtUtc); Assert.Null(paused.ErrorCode); Assert.True(paused.LastProgressAtUtc > DateTime.UtcNow.AddMinutes(-1));
        await services.DisposeAsync();
    }

    [Theory]
    [InlineData(429)]
    [InlineData(403)]
    public async Task QuotaWaitAndAccessFailureShareExistingMethodBudgetWithoutDuplicateJobs(int code)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (_, store) = await Seed(db);
        var now = DateTime.UtcNow;
        db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "product_orders", Status = "pending", StartDate = Day.AddDays(-1), EndDate = Day,
            CampaignIdsJson = JsonSerializer.Serialize(new[] { long.Parse(Day.AddDays(-1).ToString("yyyyMMdd")) }), CreatedAtUtc = now, UpdatedAtUtc = now, NextAttemptAtUtc = now.AddSeconds(-1) });
        await db.SaveChangesAsync(); var client = new FunnelFailure(code);
        await using var services = new ServiceCollection().AddDbContext<EcomadsDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()))
            .AddSingleton<IWbPromotionClient>(new UnusedPromotion()).AddSingleton<IWbTokenService>(new Tokens())
            .AddSingleton<IWbSalesFunnelClient>(client).BuildServiceProvider();
        var worker = new WbSyncWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<WbSyncWorker>.Instance);
        await worker.ProcessNextAsync(default); db.ChangeTracker.Clear();
        var job = await db.WbSyncJobs.SingleAsync(); Assert.Equal($"wb_{code}", job.ErrorCode); Assert.False((await db.Stores.SingleAsync()).AutoRefreshBlocked);
        if (code == 429)
        {
            Assert.Equal("waiting", job.Stage); Assert.Equal(0, job.AttemptCount);
            Assert.True(job.NextAttemptAtUtc >= now.AddMinutes(40));
            Assert.False(await WbRateLimits.TryReserveAsync(db, store.Id, "funnel_month", 0, now.AddMinutes(31), default));
        }
        else Assert.Equal("failed", job.Status);
        var planner = new WbSyncPlanner(db, null!, null!, NullLogger<WbSyncPlanner>.Instance);
        await new WbProductOrdersRefresh(db, planner, TimeProvider.System).RunAsync(store, default);
        Assert.Single(await db.WbSyncJobs.ToArrayAsync()); Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task EmptyOrInvalidNewerResponseWithholdsOldTotalsInsteadOfTurningAbsenceIntoZero()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (_, store) = await Seed(db);
        await Apply(db, Snapshot(store, [42], at: DateTime.UtcNow.AddHours(-2))); db.ChangeTracker.Clear();
        var empty = Snapshot(store, []); await Apply(db, empty);
        Assert.Equal("no_data", empty.ProductOrdersError); Assert.Equal(2, await db.WbProductDailyOrders.CountAsync());
        var total = (await new WbProductTotals(db).GetAsync(store.Id, ["42"], Day, Day, default))["42"];
        Assert.Null(total.OrderSum);
    }

    private sealed class FunnelFailure(int code) : IWbSalesFunnelClient
    {
        public int Calls;
        public Task<JsonDocument> GetProductsAsync(string token, DateOnly day, DateOnly pastDay, int offset, CancellationToken ct)
        { Calls++; throw new WbApiException((HttpStatusCode)code, TimeSpan.FromMinutes(45)); }
        public Task<JsonDocument> GetGroupedHistoryAsync(string token, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AutomaticRefreshOffMakesNoRequestsButManualRefreshIsDurableAndCoalesced()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync(); var (_, store) = await Seed(db);
        store.AutoRefreshEnabled = false; await db.SaveChangesAsync();
        var planner = new WbSyncPlanner(db, null!, null!, NullLogger<WbSyncPlanner>.Instance);
        var refresh = new WbProductOrdersRefresh(db, planner, TimeProvider.System);
        await refresh.RunAsync(store, default); Assert.Empty(await db.WbSyncJobs.ToArrayAsync());
        await WbProductOrdersRefresh.RequestManualAsync(db, store.Id, DateTime.UtcNow, default);
        await WbProductOrdersRefresh.RequestManualAsync(db, store.Id, DateTime.UtcNow, default);
        await refresh.RunAsync(store, default); await refresh.RunAsync(store, default);
        Assert.Single(await db.WbSyncJobs.ToArrayAsync());
        var request = await db.WbRefreshSchedules.SingleAsync(); Assert.Equal(request.RequestedSlotUtc, request.PlannedSlotUtc);
    }
    private sealed class Tokens : IWbTokenService
    {
        public string Unprotect(string token) => token;
        public string Protect(string token) => token;
        public WbTokenClaims ReadClaims(string token) => throw new NotSupportedException();
    }
    private sealed class UnusedPromotion : IWbPromotionClient
    {
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetFullStatsAsync(string token, IReadOnlyList<long> ids, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetCostsAsync(string token, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetNormQueryStatsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
    }
}

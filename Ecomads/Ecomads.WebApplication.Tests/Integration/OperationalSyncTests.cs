using System.Net;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class OperationalSyncTests(PostgresFixture postgres)
{
    private static async Task<Store> Seed(EcomadsDbContext db)
    {
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Test", ApiKey = "protected" };
        db.Sellers.Add(seller); db.Stores.Add(store);
        db.Campaigns.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = "123", Name = "Campaign", WbStatus = 9 });
        await db.SaveChangesAsync(); return store;
    }
    private static OperationalSync Sync(EcomadsDbContext db, FakeClient client, int tokenType = 4, bool stockEnabled = true)
    {
        var tokens = new Tokens(tokenType); var promotion = new Promotion();
        return new(db, client, promotion, tokens, NullLogger<OperationalSync>.Instance,
            new WbSyncPlanner(db, promotion, tokens, NullLogger<WbSyncPlanner>.Instance),
            Options.Create(new RecommendationOptions { Rule01Enabled = stockEnabled }));
    }

    [Fact]
    public async Task ReleaseDoesNotRequestStocksDemandOrReportsButStillLoadsBudgets()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await Seed(db); var client = new FakeClient();
        await Sync(db, client, 1, stockEnabled: false).RefreshAsync(store, DateTime.UtcNow, default);
        Assert.Empty(client.Offsets); Assert.Empty(client.ReportStages); Assert.False(client.DirectFbwCalled);
        var snapshots = await db.WbOperationalSnapshots.ToListAsync();
        Assert.Equal(3, snapshots.Count);
        Assert.DoesNotContain(snapshots, x => x.Source is "stocks_fbw" or "stocks_fbs" or "demand");
        Assert.Contains(snapshots, x => x.Source == "budgets" && x.State == "ready");
    }

    [Fact]
    public async Task SourcesAreIndependentAndCoveredSnapshotsAreNotRepeated()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await Seed(db); var client = new FakeClient(); var sync = Sync(db, client); var now = DateTime.UtcNow;
        await sync.RefreshAsync(store, now, default);
        Assert.Equal(6, await db.WbOperationalSnapshots.CountAsync());
        Assert.All(await db.WbOperationalSnapshots.ToListAsync(), x => Assert.Equal("ready", x.State));
        using var balance = JsonDocument.Parse((await db.WbOperationalSnapshots.SingleAsync(x => x.Source == "balance")).PayloadJson);
        Assert.Equal(30, balance.RootElement.GetProperty("amount").GetDecimal()); // Bonuses are not transferable funds.
        var calls = client.Calls;
        await sync.RefreshAsync(store, now.AddMinutes(2), default);
        Assert.Equal(calls, client.Calls);
        client.FailFbw = true;
        await sync.RefreshAsync(store, now.AddMinutes(31), default);
        var failed = await db.WbOperationalSnapshots.Where(x => x.Source == "stocks_fbw").OrderByDescending(x => x.RequestedAtUtc).FirstAsync();
        Assert.Equal("access_denied", failed.State); Assert.Equal("http_403", failed.ErrorCode);
        Assert.Equal("{}", failed.PayloadJson);
        Assert.Equal("ready", (await db.WbOperationalSnapshots.Where(x => x.Source == "stocks_fbs").OrderByDescending(x => x.RequestedAtUtc).FirstAsync()).State);
    }

    [Fact]
    public async Task InvalidBudgetDoesNotDiscardOtherCampaignBudgets()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await Seed(db);
        db.Campaigns.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = "456", Name = "Other", WbStatus = 9 });
        await db.SaveChangesAsync();
        await Sync(db, new FakeClient { InvalidSecondBudget = true }, stockEnabled: false).RefreshAsync(store, DateTime.UtcNow, default);
        var budgets = await db.WbOperationalSnapshots.Where(x => x.Source == "budgets").ToListAsync();
        Assert.Equal("ready", budgets.Single(x => x.EntityKey == "123").State);
        var invalid = budgets.Single(x => x.EntityKey == "456");
        Assert.Equal("source_error", invalid.State); Assert.Equal("invalid_source", invalid.ErrorCode);
        Assert.Equal("{}", invalid.PayloadJson);
    }

    [Fact]
    public async Task MissingBudgetPairNeverBecomesZeroBudget()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await Seed(db); var client = new FakeClient { MissingBudget = true };
        await Sync(db, client).RefreshAsync(store, DateTime.UtcNow, default);
        var budget = await db.WbOperationalSnapshots.SingleAsync(x => x.Source == "budgets");
        Assert.Equal("123", budget.EntityKey); Assert.Equal("source_error", budget.State);
        Assert.Equal("pair_missing", budget.ErrorCode); Assert.Equal("{}", budget.PayloadJson);
        Assert.Equal("ready", (await db.WbOperationalSnapshots.SingleAsync(x => x.Source == "balance")).State);
    }

    [Fact]
    public async Task FunnelPaginationResumesOnceAndRejectsRepeatedProducts()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await Seed(db); var client = new FakeClient { FullDemandPage = true }; var now = DateTime.UtcNow;
        var sync = Sync(db, client);
        await sync.RefreshAsync(store, now, default);
        Assert.Equal("collecting", (await db.WbOperationalSnapshots.SingleAsync(x => x.Source == "demand")).State);
        await sync.RefreshAsync(store, now.AddMinutes(2), default);
        Assert.Equal(new[] { 0 }, client.Offsets);
        client.FullDemandPage = false;
        await sync.RefreshAsync(store, now.AddMinutes(31), default);
        Assert.Equal(new[] { 0, 1000 }, client.Offsets);
        var latest = await db.WbOperationalSnapshots.Where(x => x.Source == "demand").OrderByDescending(x => x.RequestedAtUtc).FirstAsync();
        Assert.Equal("source_error", latest.State); // The second page repeats nmId 1.
        Assert.Equal("invalid_source", latest.ErrorCode);
    }

    [Fact]
    public async Task BasicTokenUsesDurableReportStagesWithoutCatalogRegistration()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var store = await Seed(db); var client = new FakeClient(); var now = DateTime.UtcNow;
        await Sync(db, client, 1).RefreshAsync(store, now, default);
        Assert.Equal("collecting", (await db.WbOperationalSnapshots.SingleAsync(x => x.Source == "stocks_fbw")).State);
        // Recreate the service to prove that the checkpoint is persisted, not held in process memory.
        await Sync(db, client, 1).RefreshAsync(store, now.AddMinutes(2), default);
        await Sync(db, client, 1).RefreshAsync(store, now.AddMinutes(4), default);
        var ready = await db.WbOperationalSnapshots.Where(x => x.Source == "stocks_fbw").OrderByDescending(x => x.RequestedAtUtc).FirstAsync();
        Assert.Equal("ready", ready.State); Assert.Equal(now, ready.CoverageStartedAtUtc);
        Assert.Equal(new[] { "create", "status", "download" }, client.ReportStages);
        Assert.False(client.DirectFbwCalled);
        using var payload = JsonDocument.Parse(ready.PayloadJson);
        Assert.Equal(7, payload.RootElement[0].GetProperty("quantity").GetDecimal());
    }

    private sealed class Tokens(int type = 4) : IWbTokenService
    {
        public WbTokenClaims ReadClaims(string t) => new("test", DateTime.UtcNow.AddDays(1), true, true, type);
        public string Protect(string t) => t;
        public string Unprotect(string t) => "read-only";
    }
    private sealed class FakeClient : IWbOperationsClient
    {
        public int Calls; public bool FailFbw, MissingBudget, FullDemandPage, InvalidSecondBudget;
        public bool DirectFbwCalled;
        public List<int> Offsets { get; } = [];
        public List<string> ReportStages { get; } = [];
        public Task<JsonDocument> CreateFbwReportAsync(string token, CancellationToken ct)
        { ReportStages.Add("create"); return Task.FromResult(JsonDocument.Parse("{\"data\":{\"taskId\":\"219eaecf-e532-4bd8-9f15-8036ec1b042d\"}}")); }
        public Task<JsonDocument> FbwReportStatusAsync(string token, Guid taskId, CancellationToken ct)
        { ReportStages.Add("status"); return Task.FromResult(JsonDocument.Parse("{\"data\":{\"status\":\"done\"}}")); }
        public Task<JsonDocument> DownloadFbwReportAsync(string token, Guid taskId, CancellationToken ct)
        { ReportStages.Add("download"); return Task.FromResult(JsonDocument.Parse("""[{"nmId":1,"barcode":"123","techSize":"0","warehouses":[{"warehouseName":"Total in warehouses","quantity":7},{"warehouseName":"To the client","quantity":100}]}]""")); }
        public Task<JsonDocument> StocksAsync(string t, bool fbs, int offset, CancellationToken ct)
        {
            Calls++; if (!fbs) DirectFbwCalled = true;
            if (!fbs && FailFbw) throw new WbApiException(HttpStatusCode.Forbidden);
            return Task.FromResult(JsonDocument.Parse("""{"data":{"items":[{"nmId":1,"chrtId":1,"warehouseId":1,"quantity":4,"inWayToClient":999}]}}"""));
        }
        public Task<JsonDocument> BudgetsAsync(string t, long[] ids, CancellationToken ct)
        { Calls++; return Task.FromResult(JsonDocument.Parse(InvalidSecondBudget ? """{"adverts":[{"advertId":456,"currency":"RUB","total":-1},{"advertId":123,"currency":"RUB","total":50}]}""" : MissingBudget ? "{\"adverts\":[]}" : "{\"adverts\":[{\"advertId\":123,\"currency\":\"RUB\",\"total\":50}]}")); }
        public Task<JsonDocument> BalanceAsync(string t, CancellationToken ct)
        { Calls++; return Task.FromResult(JsonDocument.Parse("{\"currency\":\"RUB\",\"balance\":10,\"net\":20,\"bonus\":9999}")); }
        public Task<JsonDocument> DemandAsync(string t, DateOnly start, DateOnly end, int offset, CancellationToken ct)
        {
            Calls++; Offsets.Add(offset);
            return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(new { data = new { currency = "RUB", products =
                Enumerable.Range(1, FullDemandPage ? 1000 : 1).Select(id => new { product = new { nmId = id }, statistic = new { selected = new { orderCount = 14 } } }) } })));
        }
    }
    private sealed class Promotion : IWbPromotionClient
    {
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string t, CancellationToken ct) => Task.FromResult<IReadOnlyList<WbCampaignInfo>>([new(123, "Campaign", 9, "cpm", [1], BidType: "unified")]);
        public Task<JsonDocument> GetCostsAsync(string t, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetFullStatsAsync(string t, IReadOnlyList<long> ids, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetNormQueryStatsAsync(string t, IReadOnlyList<WbNormQueryPair> pairs, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
    }
}

using System.Net;
using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class AdvertisingSettingsTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingsReuseImportedMetadataValidateUnitsAndDistinguishMissingFailedAndEmptySources(bool kopecks)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "S", ApiKey = "protected" };
        db.Sellers.Add(seller); db.Stores.Add(store); await db.SaveChangesAsync();
        var client = new FakeClient { Kopecks = kopecks }; var tokens = new FakeTokens();
        var planner = new WbSyncPlanner(db, client, tokens, NullLogger<WbSyncPlanner>.Instance);
        await planner.UpsertCampaignsAsync(store, [new(123, "C", 9, "cpm", [1], BidType: "manual"),
            new(124, "CPC", 9, "cpc", [2], BidType: "manual"),
            new(125, "Archived", 7, "cpm", [3], BidType: "manual")], default);
        var sync = new AdvertisingSettingsSync(db, client, tokens, NullLogger<AdvertisingSettingsSync>.Instance);
        var now = DateTime.UtcNow;
        await sync.RefreshAsync(store, now, default);
        var campaignId = await db.Campaigns.Where(x => x.WbCampaignId == "123").Select(x => x.Id).SingleAsync();
        var row = await db.WbAdvertisingSettings.SingleAsync(x => x.CampaignId == campaignId);
        var control = AdvertisingSettingsPayload.Control(row, "1", "cluster", now);
        Assert.Equal("ready", control.Status); Assert.Equal(105m, control.CurrentBid);
        Assert.Equal(false, control.Excluded); Assert.Null(control.MinimumBid); Assert.Null(control.BidStep);
        Assert.Equal(now, row.ReceivedAtUtc);
        var firstRequests = client.Requests.Count;
        await sync.RefreshAsync(store, now.AddMinutes(1), default);
        Assert.Equal(firstRequests, client.Requests.Count);
        client.MissingPair = true;
        await sync.RefreshAsync(store, now.AddHours(2), default);
        Assert.Equal("ready", row.Status);
        Assert.Equal("ready", AdvertisingSettingsPayload.Control(row, "1", "cluster", now.AddHours(2)).Status);
        Assert.False(AdvertisingSettingsPayload.Control(row, "1", "cluster", now.AddHours(2)).Excluded);
        client.MissingPair = false; client.Error = true;
        await sync.RefreshAsync(store, now.AddHours(4), default);
        Assert.Equal("access_denied", row.Status);
        Assert.Equal("access_denied", AdvertisingSettingsPayload.Control(row, "1", "cluster", now.AddHours(4)).Status);
        Assert.True(client.TokenValidated);
        Assert.Equal(0, client.CampaignCalls); // Does not steal the campaign import's rate slot.
        Assert.All(await db.WbAdvertisingSettings.Where(x => x.CampaignId != campaignId).ToArrayAsync(), x =>
        { Assert.NotEqual("source_error", x.Status); Assert.NotEqual("access_denied", x.Status); });
        client.Error = false; client.UnrequestedPair = true;
        await sync.RefreshAsync(store, now.AddHours(6), default);
        Assert.Equal("source_error", row.Status);
    }

    [Fact]
    public async Task LargeAccountsRotateOneWholeBatchAndFailuresDoNotPoisonOtherCampaigns()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "S", ApiKey = "protected" };
        db.Sellers.Add(seller); db.Stores.Add(store); await db.SaveChangesAsync();
        var client = new FakeClient { BatchMode = true }; var tokens = new FakeTokens();
        var planner = new WbSyncPlanner(db, client, tokens, NullLogger<WbSyncPlanner>.Instance);
        await planner.UpsertCampaignsAsync(store, [
            new(123, "A", 9, "cpm", Enumerable.Range(1,100).Select(x => (long)x).ToArray(), BidType: "manual"),
            new(124, "B", 11, "cpm", Enumerable.Range(101,100).Select(x => (long)x).ToArray(), BidType: "unified")], default);
        var sync = new AdvertisingSettingsSync(db, client, tokens, NullLogger<AdvertisingSettingsSync>.Instance);
        var now = DateTime.UtcNow;
        await sync.RefreshAsync(store, now, default);
        Assert.Equal(2, client.Requests.Count);
        Assert.All(client.Requests, pairs => Assert.Equal(100, pairs.Length));
        var ready = await db.WbAdvertisingSettings.SingleAsync(x => x.Status == "ready");
        client.Error = true;
        await sync.RefreshAsync(store, now.AddMinutes(13), default);
        Assert.Equal(3, client.Requests.Count);
        Assert.NotEqual(client.Requests[0][0].AdvertId, client.Requests[2][0].AdvertId);
        Assert.Equal("ready", ready.Status);
        Assert.Equal(now, ready.ReceivedAtUtc);
        Assert.Single(await db.WbAdvertisingSettings.Where(x => x.Status == "access_denied").ToArrayAsync());
    }

    private sealed class FakeTokens : IWbTokenService
    {
        public WbTokenClaims ReadClaims(string token) => new("test", DateTime.UtcNow.AddDays(1), false, false);
        public string Protect(string token) => token;
        public string Unprotect(string token) => "read-only";
    }
    private sealed class FakeClient : IWbPromotionClient
    {
        public bool MissingPair { get; set; }
        public bool Kopecks { get; set; }
        public bool BatchMode { get; set; }
        public bool UnrequestedPair { get; set; }
        public bool Error { get; set; }
        public bool TokenValidated { get; private set; }
        public int CampaignCalls { get; private set; }
        public List<WbNormQueryPair[]> Requests { get; } = [];
        public Task<JsonDocument> GetClusterSettingsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs, bool minus, CancellationToken ct)
        {
            Requests.Add(pairs.ToArray());
            TokenValidated = token == "read-only";
            if (!BatchMode) Assert.Equal(new WbNormQueryPair(123, 1), Assert.Single(pairs));
            if (Error) throw new WbApiException(HttpStatusCode.Forbidden);
            if (UnrequestedPair) return Task.FromResult(JsonDocument.Parse("{\"bids\":[{\"advert_id\":999,\"nm_id\":1,\"norm_query\":\"x\",\"bid\":100}]}"));
            if (BatchMode) return Task.FromResult(JsonDocument.Parse(minus
                ? JsonSerializer.Serialize(new { items = pairs.Select(x => new { advert_id = x.AdvertId, nm_id = x.NmId, norm_queries = Array.Empty<string>() }) })
                : "{\"bids\":[]}"));
            return Task.FromResult(JsonDocument.Parse(minus ? MissingPair ? "{\"items\":[]}" :
                "{\"items\":[{\"advert_id\":123,\"nm_id\":1,\"norm_queries\":[]}]}" :
                Kopecks ? "{\"bids\":[{\"advert_id\":123,\"nm_id\":1,\"norm_query\":\"cluster\",\"bid\":105,\"bid_kopecks\":10500,\"currency\":\"RUB\"}]}" :
                "{\"bids\":[{\"advert_id\":123,\"nm_id\":1,\"norm_query\":\"cluster\",\"bid\":105}]}"));
        }
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string t, CancellationToken ct) { CampaignCalls++; throw new InvalidOperationException(); }
        public Task<JsonDocument> GetCostsAsync(string t, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetFullStatsAsync(string t, IReadOnlyList<long> ids, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetNormQueryStatsAsync(string t, IReadOnlyList<WbNormQueryPair> pairs, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
    }
}

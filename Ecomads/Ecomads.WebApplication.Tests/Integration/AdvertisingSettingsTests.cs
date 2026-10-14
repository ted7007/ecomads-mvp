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
    [Fact]
    public async Task SettingsReuseImportedMetadataValidateUnitsAndDistinguishMissingFailedAndEmptySources()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "S", ApiKey = "protected" };
        db.Sellers.Add(seller); db.Stores.Add(store); await db.SaveChangesAsync();
        var client = new FakeClient(); var tokens = new FakeTokens();
        var planner = new WbSyncPlanner(db, client, tokens, NullLogger<WbSyncPlanner>.Instance);
        await planner.UpsertCampaignsAsync(store, [new(123, "C", 9, "cpm", [1], BidType: "manual")], default);
        var sync = new AdvertisingSettingsSync(db, client, tokens, NullLogger<AdvertisingSettingsSync>.Instance);
        var now = DateTime.UtcNow;
        await sync.RefreshAsync(store, now, default);
        var row = await db.WbAdvertisingSettings.SingleAsync();
        var control = AdvertisingSettingsPayload.Control(row, "1", "cluster", now);
        Assert.Equal("ready", control.Status); Assert.Equal(105m, control.CurrentBid);
        Assert.Equal(false, control.Excluded); Assert.Null(control.MinimumBid); Assert.Null(control.BidStep);
        Assert.Equal(now, row.ReceivedAtUtc);
        client.MissingPair = true;
        await sync.RefreshAsync(store, now.AddHours(2), default);
        Assert.Equal("unknown", row.Status);
        client.MissingPair = false; client.Error = true;
        await sync.RefreshAsync(store, now.AddHours(4), default);
        Assert.Equal("access_denied", row.Status);
        Assert.Equal("access_denied", AdvertisingSettingsPayload.Control(row, "1", "cluster", now.AddHours(4)).Status);
        Assert.True(client.TokenValidated);
        Assert.Equal(0, client.CampaignCalls); // Does not steal the campaign import's rate slot.
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
        public bool Error { get; set; }
        public bool TokenValidated { get; private set; }
        public int CampaignCalls { get; private set; }
        public Task<JsonDocument> GetClusterSettingsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs, bool minus, CancellationToken ct)
        {
            TokenValidated = token == "read-only";
            Assert.Equal(new WbNormQueryPair(123, 1), Assert.Single(pairs));
            if (Error) throw new WbApiException(HttpStatusCode.Forbidden);
            return Task.FromResult(JsonDocument.Parse(minus ? MissingPair ? "{\"items\":[]}" :
                "{\"items\":[{\"advert_id\":123,\"nm_id\":1,\"norm_queries\":[]}]}" :
                "{\"bids\":[{\"advert_id\":123,\"nm_id\":1,\"norm_query\":\"cluster\",\"bid\":10500}]}"));
        }
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string t, CancellationToken ct) { CampaignCalls++; throw new InvalidOperationException(); }
        public Task<JsonDocument> GetCostsAsync(string t, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetFullStatsAsync(string t, IReadOnlyList<long> ids, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonDocument> GetNormQueryStatsAsync(string t, IReadOnlyList<WbNormQueryPair> pairs, DateOnly start, DateOnly end, CancellationToken ct) => throw new NotSupportedException();
    }
}

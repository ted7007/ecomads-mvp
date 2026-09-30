using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ecomads.WebApplication.Data;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbWorkflowE2ETests(PostgresFixture postgres)
{
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
            targetDrr = 27, minClicks = 25, minSpend = 400, minOrders = 2, deviationPercent = 40
        });
        Assert.Equal(HttpStatusCode.OK, norms.StatusCode);

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

        Guid campaignId;
        await using (var db = postgres.CreateDbContext(connection))
        {
            campaignId = await db.Campaigns.Where(x => x.StoreId == storeId).Select(x => x.Id).SingleAsync();
        }
        using var campaignNorms = await client.PutAsJsonAsync($"/api/wb/norms/campaigns/{campaignId}", new
        {
            customName = "Моя кампания", goal = "Проверить рекламу", targetDrr = (int?)null,
            minClicks = (int?)null, minSpend = (int?)null, minOrders = (int?)null,
            deviationPercent = (int?)null
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

        using var projects = await client.GetAsync("/api/projects?startDate=2026-07-01&endDate=2026-07-01");
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
        using var projectsJson = JsonDocument.Parse(await projects.Content.ReadAsStringAsync());
        var campaign = Assert.Single(projectsJson.RootElement.EnumerateArray());
        Assert.Equal("Моя кампания", campaign.GetProperty("name").GetString());
        Assert.Equal(27, campaign.GetProperty("targetDrr").GetDecimal());
        Assert.Equal(10.25, campaign.GetProperty("kpi").GetProperty("spend").GetDouble(), 2);
        Assert.Equal(10, campaign.GetProperty("kpi").GetProperty("clicks").GetInt32());
        Assert.Equal(100, campaign.GetProperty("kpi").GetProperty("impressions").GetInt32());
    }

    private static string FakeToken(Guid sid)
    {
        static string Encode(object value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = new { sid, exp = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
            acc = 1, s = (1L << 6) | (1L << 30) };
        return $"{Encode(new { alg = "none" })}.{Encode(payload)}.1234";
    }

    private sealed class FakeWbClient : IWbPromotionClient
    {
        public Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WbCampaignInfo>>([new WbCampaignInfo(35174765, "WB campaign", 9, "cpm", [123])]);

        public Task<JsonDocument> GetFullStatsAsync(string token, IReadOnlyList<long> campaignIds,
            DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            Task.FromResult(JsonDocument.Parse("""
                [{"advertId":35174765,"days":[{"date":"2026-07-01T00:00:00Z","sum":10.25,
                "sum_price":100,"views":100,"clicks":10,"atbs":2,"orders":1,"canceled":0,
                "apps":[{"nms":[{"nmId":123,"name":"Item","sum":10.25,"sum_price":100,
                "views":100,"clicks":10,"atbs":2,"orders":1,"canceled":0}]}]}]}]
                """));

        public Task<JsonDocument> GetNormQueryStatsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs,
            DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            Task.FromResult(JsonDocument.Parse("""{"items":[]}"""));
    }

    private sealed class WbFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWbPromotionClient>();
                services.AddSingleton<IWbPromotionClient, FakeWbClient>();
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

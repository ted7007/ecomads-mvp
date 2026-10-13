using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class RecommendationEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task HttpEndpointMatchesSnapshotTableAndInvalidatesAfterArticleNormChange()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Internal", ApiKey = "test", AutoRefreshEnabled = false };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = "123", Name = "C", WbStatus = 9 };
        var article = new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id, WbNomenclatureId = "1", Name = "Product" };
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            var date = new DateOnly(2026, 10, 1);
            db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign); db.Nomenclatures.Add(article);
            db.CampaignNomenclatureStatistics.Add(new() { CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Revenue = 20000, OrderedProducts = 10, RecommendationFieldsVerified = true });
            db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = date, Result = "data", Spend = 800 });
            db.WbClusterStatistics.Add(new() { Id = Guid.NewGuid(), CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date, ClusterName = "zero", Spend = 800, Clicks = 10, Orders = 99, OrderedProducts = 0 });
            db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed",
                StartDate = date, EndDate = date, PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
            await db.SaveChangesAsync();
        }
        var url = $"/api/wb/campaigns/{campaign.Id}/recommendations?startDate=2026-10-01&endDate=2026-10-01";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Seller", seller.Id.ToString());
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var first = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = first.RootElement;
        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.Equal("2026-10-01", body.GetProperty("window").GetProperty("startDate").GetString());
        var card = Assert.Single(body.GetProperty("cards").EnumerateArray());
        Assert.Equal("R03", card.GetProperty("rule").GetProperty("ruleId").GetString());
        Assert.Equal(800, card.GetProperty("rule").GetProperty("threshold").GetDecimal());
        var row = Assert.Single(body.GetProperty("rows").EnumerateArray());
        Assert.Equal(card.GetProperty("facts").GetProperty("spend").GetDecimal(), row.GetProperty("spend").GetDecimal());
        Assert.Equal(99, row.GetProperty("orders").GetInt32());
        Assert.Equal(0, row.GetProperty("orderedProducts").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("estimatedDrr").ValueKind);
        var runId = body.GetProperty("runId").GetString();
        var normUrl = $"/api/wb/norms/campaigns/{campaign.Id}/articles/{article.Id}";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(normUrl, new { targetDrr = 40 })).StatusCode);
        using var changed = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Empty(changed.RootElement.GetProperty("cards").EnumerateArray());
        Assert.NotEqual(runId, changed.RootElement.GetProperty("runId").GetString());
        using var snapshot = JsonDocument.Parse(await client.GetStringAsync($"/api/wb/campaigns/{campaign.Id}/recommendations/runs/{runId}"));
        Assert.Single(snapshot.RootElement.GetProperty("cards").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url.Replace("startDate=2026-10-01", "startDate=invalid"))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Seller");
        client.DefaultRequestHeaders.Add("X-Test-Seller", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/wb/campaigns/{campaign.Id}/recommendations/runs/{runId}")).StatusCode);
    }

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["Wb:AutoRefresh:Enabled"] = "false",
                ["Wb:Recommendations:Enabled"] = "true",
                ["Wb:Recommendations:SourcesComparable"] = "true"
            }));
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
            { options.DefaultAuthenticateScheme = "RecommendationTest"; options.DefaultChallengeScheme = "RecommendationTest"; })
                .AddScheme<AuthenticationSchemeOptions, TestAuth>("RecommendationTest", _ => { }));
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Seller", out var seller)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, seller.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

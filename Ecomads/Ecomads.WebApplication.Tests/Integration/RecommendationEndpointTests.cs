using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Recommendations;
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
public sealed partial class RecommendationEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task HttpEndpointMatchesSnapshotTableAndInvalidatesAfterCampaignNormChange()
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
            var date = RecommendationPeriods.Economic(DateTime.UtcNow).EndDate;
            TestData.AddConfirmedClusterDay(db, store.Id, campaign.Id, article.Id, date);
            db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign); db.Nomenclatures.Add(article);
            db.CampaignNomenclatureStatistics.Add(new() { CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Revenue = 20000, OrderedProducts = 10, RecommendationFieldsVerified = true });
            db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = date, Result = "data", Spend = 800 });
            db.WbClusterStatistics.Add(new() { Id = Guid.NewGuid(), CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date, ClusterName = "zero", Spend = 800, Clicks = 10, Orders = 99, OrderedProducts = 0 });
            db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed",
                StartDate = date, EndDate = date, PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
            for (var day = date.AddDays(-13); day < date; day = day.AddDays(1))
            {
                TestData.AddConfirmedClusterDay(db, store.Id, campaign.Id, article.Id, day);
                db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = day, Result = "zero", Spend = 0 });
                db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed", StartDate = day, EndDate = day, PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
            }
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
        Assert.Equal(RecommendationPeriods.Economic(DateTime.UtcNow).StartDate.ToString("yyyy-MM-dd"), body.GetProperty("window").GetProperty("startDate").GetString());
        using var otherDates = JsonDocument.Parse(await client.GetStringAsync(url.Replace("2026-10-01", "2025-01-01")));
        Assert.Equal(body.GetProperty("runId").GetGuid(), otherDates.RootElement.GetProperty("runId").GetGuid());
        var card = Assert.Single(body.GetProperty("cards").EnumerateArray());
        Assert.Equal("R03", card.GetProperty("rule").GetProperty("ruleId").GetString());
        Assert.Equal(2, card.GetProperty("rule").GetProperty("ruleVersion").GetInt32());
        var clickEvidence = card.GetProperty("presentation").GetProperty("clickEvidence");
        Assert.Equal("fallback", clickEvidence.GetProperty("source").GetString());
        Assert.Equal(150, clickEvidence.GetProperty("minimumClicks").GetInt32());
        Assert.Equal("insufficient", clickEvidence.GetProperty("result").GetString());
        Assert.Equal("watch", card.GetProperty("presentation").GetProperty("primary").GetProperty("kind").GetString());
        Assert.Equal(800, card.GetProperty("rule").GetProperty("threshold").GetDecimal());
        var row = Assert.Single(body.GetProperty("rows").EnumerateArray());
        Assert.Equal(card.GetProperty("facts").GetProperty("spend").GetDecimal(), row.GetProperty("spend").GetDecimal());
        Assert.Equal(99, row.GetProperty("orders").GetInt32());
        Assert.Equal(0, row.GetProperty("orderedProducts").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("estimatedDrr").ValueKind);
        var runId = body.GetProperty("runId").GetString();
        var normUrl = $"/api/wb/norms/campaigns/{campaign.Id}";
        var retired = await client.PutAsJsonAsync($"{normUrl}/articles/{article.Id}", new { targetDrr = 40 });
        Assert.True(retired.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(normUrl, new { targetDrr = 40 })).StatusCode);
        using var changed = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Empty(changed.RootElement.GetProperty("cards").EnumerateArray());
        Assert.NotEqual(runId, changed.RootElement.GetProperty("runId").GetString());
        using var snapshot = JsonDocument.Parse(await client.GetStringAsync($"/api/wb/campaigns/{campaign.Id}/recommendations/runs/{runId}"));
        Assert.Single(snapshot.RootElement.GetProperty("cards").EnumerateArray());
        Assert.Equal(clickEvidence.GetRawText(), snapshot.RootElement.GetProperty("cards")[0]
            .GetProperty("presentation").GetProperty("clickEvidence").GetRawText());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url.Replace("startDate=2026-10-01", "startDate=invalid"))).StatusCode); // Obsolete query fields are ignored.
        client.DefaultRequestHeaders.Remove("X-Test-Seller");
        client.DefaultRequestHeaders.Add("X-Test-Seller", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/wb/campaigns/{campaign.Id}/recommendations/runs/{runId}")).StatusCode);
    }

    [Fact]
    public async Task PackageDecisionsSaveOnlySelectedEvidenceAndSummaryHasTopFive()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "S", ApiKey = "test", AutoRefreshEnabled = false };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = "123", Name = "C", WbStatus = 9 };
        var article = new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id, WbNomenclatureId = "1", Name = "P" };
        var date = RecommendationPeriods.Economic(DateTime.UtcNow).EndDate;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign); db.Nomenclatures.Add(article);
            TestData.AddConfirmedClusterDay(db, store.Id, campaign.Id, article.Id, date);
            db.CampaignNomenclatureStatistics.Add(new() { CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Revenue = 10000, OrderedProducts = 10, RecommendationFieldsVerified = true });
            db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = date, Result = "data", Spend = 6000 });
            for (var i = 0; i < 6; i++) db.WbClusterStatistics.Add(new() { Id = Guid.NewGuid(), CampaignId = campaign.Id, NomenclatureId = article.Id,
                Date = date, ClusterName = "cluster" + i, Spend = 1000, Clicks = 150, Orders = 0, OrderedProducts = 0 });
            db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed", StartDate = date, EndDate = date, PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
            db.WbAdvertisingSettings.Add(new() { CampaignId = campaign.Id, Status = "ready", BidType = "unified", PaymentType = "cpm", ReceivedAtUtc = DateTime.UtcNow,
                PayloadJson = "{\"bids\":[],\"minus\":[{\"nomenclatureId\":\"1\",\"clusters\":[]}]}" });
            for (var day = date.AddDays(-13); day < date; day = day.AddDays(1))
            {
                TestData.AddConfirmedClusterDay(db, store.Id, campaign.Id, article.Id, day);
                db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = day, Result = "zero", Spend = 0 });
                db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed", StartDate = day, EndDate = day, PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
            }
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-Seller", seller.Id.ToString());
        var url = $"/api/wb/campaigns/{campaign.Id}/recommendations?startDate=2026-10-01&endDate=2026-10-01";
        using var initial = JsonDocument.Parse(await client.GetStringAsync(url));
        var report = initial.RootElement;
        Assert.Equal(3, report.GetProperty("schemaVersion").GetInt32());
        var bundle = Assert.Single(report.GetProperty("packages").EnumerateArray());
        Assert.Equal(6, bundle.GetProperty("evaluationIds").GetArrayLength());
        Assert.Equal(3000m, bundle.GetProperty("weeklySpend").GetDecimal());
        var ids = bundle.GetProperty("evaluationIds").EnumerateArray().Take(2).Select(x => x.GetGuid()).ToArray();
        var run = report.GetProperty("runId").GetGuid();
        var decisionUrl = $"/api/wb/campaigns/{campaign.Id}/recommendations/runs/{run}/decisions";
        using var savedResponse = await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = ids.Concat(ids), ruleId = "R03", status = "accepted", comment = "manual" });
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        using var saved = JsonDocument.Parse(await savedResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, saved.RootElement.GetProperty("cards").EnumerateArray().Count(c => c.GetProperty("decision").ValueKind != JsonValueKind.Null));
        using var retried = await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = ids, ruleId = "R03", status = "accepted", comment = "manual" });
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(2, scope.ServiceProvider.GetRequiredService<EcomadsDbContext>().WbSnapshotDecisions.Count(x => x.RunId == run));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = ids, ruleId = "R03", status = "snoozed" })).StatusCode);
        using var summary = JsonDocument.Parse(await client.GetStringAsync($"/api/wb/recommendations/summary?startDate=2026-10-01&endDate=2026-10-01&storeId={store.Id}"));
        Assert.Equal(5, summary.RootElement.GetProperty("top").GetArrayLength());
        Assert.Equal(1, summary.RootElement.GetProperty("remaining").GetInt32());
        Assert.All(summary.RootElement.GetProperty("top").EnumerateArray(), x => Assert.Equal(run, x.GetProperty("runId").GetGuid()));

        using var rejected = await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = new[] { ids[0] }, ruleId = "R03", status = "irrelevant", reason = "Не подходит" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var rejectedReport = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.DoesNotContain(rejectedReport.RootElement.GetProperty("cards").EnumerateArray(), c => c.GetProperty("evaluationId").GetGuid() == ids[0]);
        Assert.Equal(5, Assert.Single(rejectedReport.RootElement.GetProperty("packages").EnumerateArray()).GetProperty("evaluationIds").GetArrayLength());

        var tomorrow = Ecomads.WebApplication.Services.Wb.WbSyncPlanner.Today().AddDays(1);
        using var snoozed = await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = new[] { ids[1] }, ruleId = "R03", status = "snoozed", snoozeUntil = tomorrow });
        Assert.Equal(HttpStatusCode.OK, snoozed.StatusCode);
        using var updated = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Equal(4, updated.RootElement.GetProperty("cards").GetArrayLength());
        Assert.Empty(updated.RootElement.GetProperty("packages").EnumerateArray());
        Assert.DoesNotContain(updated.RootElement.GetProperty("cards").EnumerateArray(), c => ids.Contains(c.GetProperty("evaluationId").GetGuid()));
        using var updatedSummary = JsonDocument.Parse(await client.GetStringAsync($"/api/wb/recommendations/summary?storeId={store.Id}"));
        Assert.Equal(4, updatedSummary.RootElement.GetProperty("top").GetArrayLength());
        Assert.Equal(0, updatedSummary.RootElement.GetProperty("remaining").GetInt32());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            var decision = Assert.Single(db.WbSnapshotDecisions.Where(x => x.RunId == run && x.Status == "snoozed"));
            decision.SnoozeUntil = tomorrow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        using var resumed = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Contains(resumed.RootElement.GetProperty("cards").EnumerateArray(), c => c.GetProperty("evaluationId").GetGuid() == ids[1]);
        Assert.DoesNotContain(resumed.RootElement.GetProperty("cards").EnumerateArray(), c => c.GetProperty("evaluationId").GetGuid() == ids[0]);
        client.DefaultRequestHeaders.Remove("X-Test-Seller"); client.DefaultRequestHeaders.Add("X-Test-Seller", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = ids, ruleId = "R03", status = "accepted" })).StatusCode);
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

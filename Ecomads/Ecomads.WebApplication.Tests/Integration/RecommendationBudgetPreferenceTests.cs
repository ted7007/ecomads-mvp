using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

public sealed partial class RecommendationEndpointTests
{
    [Fact]
    public async Task BudgetPreferenceSurvivesRecalculationAndLegacyNormWritesAndCanBeRemoved()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Budget test", ApiKey = "test", AutoRefreshEnabled = false };
        var first = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "First", WbCampaignId = "123", WbStatus = 9 };
        var other = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Other", WbCampaignId = "124", WbStatus = 9 };
        var now = DateTime.UtcNow; var today = WbSyncPlanner.Today(now);
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.AddRange(first, other);
            foreach (var campaign in new[] { first, other })
            {
                foreach (var i in Enumerable.Range(1, 7))
                {
                    var date = today.AddDays(-i);
                    db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = date, Result = "data", Spend = 100 });
                    db.CampaignStatistics.Add(new() { CampaignId = campaign.Id, Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Spend = 100, Revenue = 1000, Orders = 5 });
                }
                db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Source = "budgets", EntityKey = campaign.WbCampaignId,
                    State = "ready", RequestedAtUtc = now, ReceivedAtUtc = now,
                    PayloadJson = JsonSerializer.Serialize(new Dictionary<string, decimal> { [campaign.WbCampaignId] = 0 }) });
            }
            db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Source = "advertising", State = "ready", RequestedAtUtc = now, ReceivedAtUtc = now,
                PayloadJson = JsonSerializer.Serialize(new[] { new WbCampaignInfo(123, "First", 9, "cpm", [], BidType: "unified"),
                    new WbCampaignInfo(124, "Other", 9, "cpm", [], BidType: "unified") }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-Seller", seller.Id.ToString());
        var url = $"/api/wb/campaigns/{first.Id}/recommendations";
        using var initial = JsonDocument.Parse(await client.GetStringAsync(url));
        var card = Assert.Single(initial.RootElement.GetProperty("cards").EnumerateArray());
        Assert.Equal("R02", card.GetProperty("rule").GetProperty("ruleId").GetString());
        var run = initial.RootElement.GetProperty("runId").GetGuid(); var evaluation = card.GetProperty("evaluationId").GetGuid();
        var decisionUrl = $"{url}/runs/{run}/decisions";
        // The code is structured and cannot be used for other decisions or rules.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = new[] { evaluation }, ruleId = "R02", status = "accepted", reasonCode = "budget_auto_replenishment" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = new[] { evaluation }, ruleId = "R03", status = "irrelevant", reasonCode = "budget_auto_replenishment" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE wb_snapshot_decisions ADD CONSTRAINT ranking_atomicity_test CHECK (\"ReasonCode\" IS DISTINCT FROM 'budget_auto_replenishment')");
        }
        try
        {
            using var failed = await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = new[] { evaluation }, ruleId = "R02", status = "irrelevant", reasonCode = "budget_auto_replenishment" });
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }
        catch (DbUpdateException) { /* TestServer may propagate the deliberately injected database failure. */ }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            Assert.False(await db.WbCampaignNorms.AnyAsync(x => x.CampaignId == first.Id));
            Assert.False(await db.WbSnapshotDecisions.AnyAsync(x => x.RunId == run));
            Assert.False(await db.WbNormRevisions.AnyAsync(x => x.CampaignId == first.Id));
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE wb_snapshot_decisions DROP CONSTRAINT ranking_atomicity_test");
        }
        using var saved = await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = new[] { evaluation }, ruleId = "R02", status = "irrelevant", reasonCode = "budget_auto_replenishment" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var hidden = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()); Assert.Empty(hidden.RootElement.GetProperty("cards").EnumerateArray());
        var normUrl = $"/api/wb/norms/campaigns/{first.Id}";
        using var norms = JsonDocument.Parse(await client.GetStringAsync(normUrl));
        Assert.True(norms.RootElement.GetProperty("overrides").GetProperty("budgetAutoReplenishmentEnabled").GetBoolean());
        // Legacy payload leaves the preference intact and causes a new engine snapshot.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(normUrl, new { targetDrr = 25 })).StatusCode);
        using var recalculated = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.NotEqual(run, recalculated.RootElement.GetProperty("runId").GetGuid());
        Assert.Empty(recalculated.RootElement.GetProperty("cards").EnumerateArray());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            var refreshedAt = DateTime.UtcNow;
            db.WbOperationalSnapshots.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Source = "budgets", EntityKey = "123",
                State = "ready", RequestedAtUtc = refreshedAt, ReceivedAtUtc = refreshedAt, PayloadJson = "{\"123\":10}" });
            await db.SaveChangesAsync();
        }
        using var refreshedBudget = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Empty(refreshedBudget.RootElement.GetProperty("cards").EnumerateArray());
        Assert.NotEqual(recalculated.RootElement.GetProperty("runId").GetGuid(), refreshedBudget.RootElement.GetProperty("runId").GetGuid());
        using (var restarted = new Factory(connection))
        using (var restartedClient = restarted.CreateClient())
        {
            restartedClient.DefaultRequestHeaders.Add("X-Test-Seller", seller.Id.ToString());
            using var afterRestart = JsonDocument.Parse(await restartedClient.GetStringAsync(url));
            Assert.Empty(afterRestart.RootElement.GetProperty("cards").EnumerateArray());
        }
        using var summary = JsonDocument.Parse(await client.GetStringAsync($"/api/wb/recommendations/summary?storeId={store.Id}"));
        Assert.Equal(other.Id, Assert.Single(summary.RootElement.GetProperty("top").EnumerateArray()).GetProperty("card").GetProperty("subject").GetProperty("campaignId").GetGuid());
        Assert.Equal(0, summary.RootElement.GetProperty("remaining").GetInt32());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
            var decision = await db.WbSnapshotDecisions.SingleAsync(x => x.RunId == run);
            Assert.Equal("Включено автопополнение", decision.Reason);
            Assert.Equal("budget_auto_replenishment", decision.ReasonCode);
            Assert.True(await db.WbCampaignNorms.AnyAsync(x => x.CampaignId == first.Id && x.BudgetAutoReplenishmentEnabled));
            Assert.Empty(await db.WbResultMeasurements.ToArrayAsync());
            Assert.Contains("R02", (await db.WbRecommendationRuns.SingleAsync(x => x.Id == run)).SnapshotJson);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(normUrl, new { targetDrr = 25, budgetAutoReplenishmentEnabled = false })).StatusCode);
        using var restored = JsonDocument.Parse(await client.GetStringAsync(url)); Assert.Single(restored.RootElement.GetProperty("cards").EnumerateArray());
        using var oldRestored = JsonDocument.Parse(await client.GetStringAsync($"{url}/runs/{run}")); Assert.Single(oldRestored.RootElement.GetProperty("cards").EnumerateArray());
        client.DefaultRequestHeaders.Remove("X-Test-Seller"); client.DefaultRequestHeaders.Add("X-Test-Seller", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(normUrl, new { budgetAutoReplenishmentEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(decisionUrl, new { evaluationIds = new[] { evaluation }, ruleId = "R02", status = "irrelevant", reasonCode = "budget_auto_replenishment" })).StatusCode);
    }

    [Fact]
    public async Task BudgetMigrationPreservesExistingNormsDecisionsAndSnapshots()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261023160908_AddPilotApplications");
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Existing" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Existing", WbCampaignId = "1" };
        db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign);
        var run = new WbRecommendationRun { Id = Guid.NewGuid(), SellerId = seller.Id, StoreId = store.Id, CampaignId = campaign.Id,
            StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 14), Fingerprint = "old", SnapshotJson = "{}", CalculatedAtUtc = DateTime.UtcNow };
        db.WbRecommendationRuns.Add(run); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO wb_campaign_norms (campaign_id, \"RecommendationMode\", target_drr, version, updated_at_utc) VALUES ({campaign.Id}, 'profit', 25, 3, {DateTime.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO wb_snapshot_decisions (\"Id\", \"RunId\", \"EvaluationId\", \"RuleId\", \"Status\", \"Reason\", \"CreatedAtUtc\") VALUES ({Guid.NewGuid()}, {run.Id}, {Guid.NewGuid()}, 'R02', 'irrelevant', 'Включено автопополнение', {DateTime.UtcNow})");
        await migrator.MigrateAsync();
        var norms = await db.WbCampaignNorms.SingleAsync();
        Assert.False(norms.BudgetAutoReplenishmentEnabled); Assert.Equal(25, norms.TargetDrr); Assert.Equal(3, norms.Version);
        Assert.Null((await db.WbSnapshotDecisions.SingleAsync()).ReasonCode);
        Assert.Equal("{}", (await db.WbRecommendationRuns.SingleAsync()).SnapshotJson);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

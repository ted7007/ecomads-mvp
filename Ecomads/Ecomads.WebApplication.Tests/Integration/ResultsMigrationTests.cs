using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class ResultsMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task UpgradePreservesExistingRunsAndStartsWithEmptyResultsHistory()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261019234424_RemoveWbArticleNorms");
        var seller = TestData.CreateRegularSeller(); var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Existing" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Existing", WbCampaignId = "123" };
        db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign);
        var run = new WbRecommendationRun { Id = Guid.NewGuid(), SellerId = seller.Id, StoreId = store.Id, CampaignId = campaign.Id,
            StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 14), Fingerprint = "existing", SnapshotJson = "{}", CalculatedAtUtc = DateTime.UtcNow };
        db.WbRecommendationRuns.Add(run); await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.True(await db.WbRecommendationRuns.AnyAsync(x => x.Id == run.Id));
        Assert.Empty(await db.WbResultRecommendations.ToArrayAsync());
        Assert.Empty(await db.WbResultMeasurements.ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

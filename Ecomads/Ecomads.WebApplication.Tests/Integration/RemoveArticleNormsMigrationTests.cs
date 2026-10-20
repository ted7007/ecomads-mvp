using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class RemoveArticleNormsMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task UpgradeRemovesOnlyRetiredOverridesAndPreservesCampaignStoreNormsAndOldSnapshots()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        await db.GetService<IMigrator>().MigrateAsync("20261019231219_AddWbProductOrders");
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "test", ApiKey = "test" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "test", WbCampaignId = "1" };
        var article = new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id, Name = "test", WbNomenclatureId = "1" };
        var date = new DateOnly(2026, 10, 1);
        db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign); db.Nomenclatures.Add(article);
        db.WbStoreNorms.Add(new() { StoreId = store.Id, TargetDrr = 30 });
        db.WbCampaignNorms.Add(new() { CampaignId = campaign.Id, TargetDrr = 25, LaunchDate = date });
        db.WbNormRevisions.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, CampaignId = campaign.Id,
            NomenclatureId = article.Id, Version = 7, SettingsJson = "{\"targetDrr\":40}" });
        var runId = Guid.NewGuid();
        var evaluation = new ClusterEvaluation(Guid.NewGuid(), new(store.Id, campaign.Id, article.Id, "1", "test", "cluster"),
            new(100, 1000, 10, 0, 1, 1, 100), new(.4m, "article", 1, 1, 7), new(true, true, true, true),
            new(100, 1, null, null, null, null), []);
        var oldReport = new RecommendationResponse(true, true, runId, 3, new(date, date), DateTime.UtcNow, "complete", "old", [], [evaluation], [], new(0, 0, 0, 0));
        db.WbRecommendationRuns.Add(new() { Id = runId, SellerId = seller.Id, StoreId = store.Id, CampaignId = campaign.Id,
            StartDate = date, EndDate = date, CalculatedAtUtc = DateTime.UtcNow, SchemaVersion = 3, Fingerprint = "historical",
            SnapshotJson = JsonSerializer.Serialize(oldReport, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO wb_article_norms
            (""CampaignId"", ""NomenclatureId"", ""TargetDrr"", ""Version"", ""UpdatedAtUtc"", ""LaunchDate"")
            VALUES ({campaign.Id}, {article.Id}, 40, 7, {DateTime.UtcNow}, {date})");
        await db.Database.MigrateAsync();
        Assert.Null(await db.Database.SqlQueryRaw<string?>("SELECT to_regclass('public.wb_article_norms')::text AS \"Value\"").SingleAsync());
        Assert.Null(db.Model.FindEntityType("Ecomads.WebApplication.Data.Models.WbArticleNorm"));
        Assert.Equal(30, (await db.WbStoreNorms.SingleAsync()).TargetDrr);
        var settings = await db.WbCampaignNorms.SingleAsync(); Assert.Equal(25, settings.TargetDrr); Assert.Equal(date, settings.LaunchDate);
        Assert.Single(await db.WbNormRevisions.ToArrayAsync());
        var service = new RecommendationService(db, Options.Create(new RecommendationOptions()), NullLogger<RecommendationService>.Instance);
        var historical = await service.SnapshotAsync(seller.Id, campaign.Id, runId, default);
        var preserved = Assert.Single(historical!.Evaluations);
        Assert.Equal("article", preserved.Norm.Source); Assert.Equal(7, preserved.Norm.ArticleVersion); Assert.Equal(.4m, preserved.Norm.Value);
        var current = RecommendationEngine.ResolveNorm(settings.TargetDrr, 30);
        Assert.Equal(.25m, current.Value); Assert.Equal("campaign", current.Source); Assert.Equal(0, current.ArticleVersion);
    }
}

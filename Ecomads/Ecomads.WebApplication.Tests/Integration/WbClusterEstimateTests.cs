using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbClusterEstimateTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ImportAndEndpoint_UseOnlyLeafUnitsAndSelectedCampaignArticlePeriod()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB", ApiKey = "test" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = "123", Name = "C" };
        db.Sellers.Add(seller); db.Stores.Add(store); db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        // Parent totals deliberately differ from leaf totals. Orders deliberately differ from shks.
        using var json = JsonDocument.Parse("""
        [{"advertId":123,"shks":999,"days":[{"date":"2026-07-01T00:00:00Z",
        "sum":100,"sum_price":99999,"shks":888,"views":100,"clicks":30,"atbs":0,"orders":77,"canceled":0,
        "apps":[{"shks":777,"nms":[{"nmId":1,"sum":20,"sum_price":200,"shks":2,"views":50,"clicks":15,"atbs":0,"orders":7,"canceled":0},
        {"nmId":2,"sum":20,"sum_price":10000,"shks":1,"views":50,"clicks":15,"atbs":0,"orders":7,"canceled":0}]},
        {"shks":666,"nms":[{"nmId":1,"sum":30,"sum_price":600,"shks":3,"views":50,"clicks":15,"atbs":0,"orders":7,"canceled":0}]}]}]}]
        """);
        var importer = new WbFullStatsImporter(db);
        await importer.ImportAsync(store.Id, json.RootElement, CancellationToken.None);
        db.ChangeTracker.Clear();
        await importer.ImportAsync(store.Id, json.RootElement, CancellationToken.None);
        db.ChangeTracker.Clear();
        var article = await db.Nomenclatures.SingleAsync(x => x.WbNomenclatureId == "1");
        var basis = await db.CampaignNomenclatureStatistics.SingleAsync(x => x.NomenclatureId == article.Id);
        Assert.Equal(5, basis.OrderedProducts);
        Assert.Equal(800m, basis.Revenue);
        Assert.Equal(99999m, (await db.CampaignStatistics.SingleAsync()).Revenue);
        var date = new DateOnly(2026, 7, 1);
        TestData.AddConfirmedClusterDay(db, store.Id, campaign.Id, article.Id, date);
        var second = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = "456", Name = "Other" };
        db.Campaigns.Add(second);
        db.CampaignNomenclatureStatistics.AddRange(
            new() { CampaignId = campaign.Id, NomenclatureId = article.Id, Date = basis.Date.AddDays(-1), Revenue = 99999, OrderedProducts = 1 },
            new() { CampaignId = second.Id, NomenclatureId = article.Id, Date = basis.Date, Revenue = 99999, OrderedProducts = 1 });
        db.WbClusterStatistics.Add(new() { Id = Guid.NewGuid(), CampaignId = campaign.Id,
            NomenclatureId = article.Id, Date = date, ClusterName = "cluster", Spend = 80, OrderedProducts = 2, Orders = 99, Clicks = 40, AveragePosition = 5.4m });
        db.WbSyncJobs.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "clusters", Status = "completed",
            StartDate = date, EndDate = date, PairIdsJson = "[{\"AdvertId\":123,\"NmId\":1}]" });
        await db.SaveChangesAsync();
        var controller = new WbCampaignClustersController(db) { ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, seller.Id.ToString())], "test")) } } };
        var response = Assert.IsType<WbCampaignClustersController.ClusterResponse>(
            Assert.IsType<OkObjectResult>(await controller.Get(campaign.Id, date, date, CancellationToken.None)).Value);
        var row = Assert.Single(response.Rows);
        Assert.Equal(5.4m, row.AveragePosition);
        Assert.Equal(1, row.PositionDays);
        Assert.Equal("complete", row.PositionStatus);
        Assert.Equal(160m, row.AverageOrderedUnitPrice);
        Assert.Equal(320m, row.EstimatedOrderSum);
        Assert.Equal(25m, row.EstimatedDrr);
        Assert.Equal("complete", row.EstimateStatus);
        Assert.Equal("Статистика выбранного периода", row.Assessment);
        var unbounded = Assert.IsType<WbCampaignClustersController.ClusterResponse>(
            Assert.IsType<OkObjectResult>(await controller.Get(campaign.Id, null, null, CancellationToken.None)).Value);
        Assert.Null(Assert.Single(unbounded.Rows).EstimatedOrderSum);
        var expanded = Assert.IsType<WbCampaignClustersController.ClusterResponse>(
            Assert.IsType<OkObjectResult>(await controller.Get(campaign.Id, date, date.AddDays(1), CancellationToken.None)).Value);
        Assert.Equal("unavailable", Assert.Single(expanded.Rows).EstimateStatus);
        // Missing shks on even one app cannot become orders or the sum of known apps.
        using var missingUnits = JsonDocument.Parse(json.RootElement.GetRawText().Replace("\"shks\":3,", ""));
        db.ChangeTracker.Clear();
        await importer.ImportAsync(store.Id, missingUnits.RootElement, CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Null((await db.CampaignNomenclatureStatistics.SingleAsync(x =>
            x.CampaignId == campaign.Id && x.NomenclatureId == article.Id && x.Date == basis.Date)).OrderedProducts);
        var unavailable = Assert.IsType<WbCampaignClustersController.ClusterResponse>(
            Assert.IsType<OkObjectResult>(await controller.Get(campaign.Id, date, date, CancellationToken.None)).Value);
        Assert.Null(Assert.Single(unavailable.Rows).EstimatedOrderSum);
    }
}

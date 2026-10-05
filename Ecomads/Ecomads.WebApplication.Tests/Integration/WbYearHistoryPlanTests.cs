using System.Text.Json;
using System.Security.Claims;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbYearHistoryPlanTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PlansYearAsBoundedRequestsAndSharesMethodSlots()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateActiveDemoSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB",
            Marketplace = "Wildberries", ApiKey = "protected-token" };
        db.Sellers.Add(seller);
        db.Stores.Add(store);
        db.Campaigns.Add(new Campaign { Id = Guid.NewGuid(), StoreId = store.Id,
            WbCampaignId = "12345", Name = "Active", WbStatus = 9 });
        await db.SaveChangesAsync();

        var planner = new WbSyncPlanner(db, null!, null!, NullLogger<WbSyncPlanner>.Instance);
        var end = WbSyncPlanner.Yesterday();
        var start = end.AddDays(-364);
        var stats = (await planner.EnqueueYearFullStatsAsync(store, start, end, CancellationToken.None)).Job!;
        var expenses = (await planner.EnqueueYearExpensesAsync(store, start, end, CancellationToken.None)).Job!;
        var orders = (await planner.EnqueueYearOrdersAsync(store, start, end, CancellationToken.None)).Job!;

        var batches = JsonSerializer.Deserialize<WbHistoryBatch[]>(stats.PairIdsJson!)!;
        Assert.Equal(12, batches.Length);
        Assert.Equal(start, batches[0].StartDate);
        Assert.Equal(end, batches[^1].EndDate);
        Assert.All(batches, x => Assert.InRange(x.EndDate.DayNumber - x.StartDate.DayNumber, 0, 30));
        var expenseBatches = JsonSerializer.Deserialize<WbHistoryBatch[]>(expenses.PairIdsJson!)!;
        Assert.Equal(13, expenseBatches.Length);
        Assert.Equal(start, expenseBatches[0].StartDate);
        Assert.Equal(end, expenseBatches[^1].EndDate);
        Assert.All(expenseBatches, x => Assert.InRange(x.EndDate.DayNumber - x.StartDate.DayNumber, 0, 28));
        Assert.Equal(179, WbSyncJobUnits.Total(orders));
        Assert.Equal("fullstats", WbRateLimits.MethodFor(stats.Kind));
        Assert.Equal("expenses", WbRateLimits.MethodFor(expenses.Kind));
        Assert.Equal("funnel_products", WbRateLimits.MethodFor(orders.Kind));
    }

    [Fact]
    public async Task YearJobCanPauseAndResumeWithoutLosingProgress()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateActiveDemoSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB",
            Marketplace = "Wildberries", ApiKey = "protected-token" };
        var job = new WbSyncJob { Id = Guid.NewGuid(), StoreId = store.Id,
            Kind = "funnel_year", Status = "running", Stage = "waiting",
            StartDate = new DateOnly(2025, 10, 4), EndDate = new DateOnly(2026, 10, 3),
            CampaignIdsJson = "[20251004,20251006]", NextCampaignOffset = 1,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            NextAttemptAtUtc = DateTime.UtcNow.AddHours(1) };
        db.Sellers.Add(seller);
        db.Stores.Add(store);
        db.WbSyncJobs.Add(job);
        await db.SaveChangesAsync();

        var controller = new WbSyncVisibilityController(db) { ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, seller.Id.ToString())], "test")) }
        } };
        Assert.IsType<OkObjectResult>(await controller.Pause(store.Id, job.Id, CancellationToken.None));
        Assert.NotNull(job.PauseRequestedAtUtc);
        Assert.Equal(1, job.NextCampaignOffset);
        Assert.IsType<OkObjectResult>(await controller.Resume(store.Id, job.Id, CancellationToken.None));
        Assert.Null(job.PauseRequestedAtUtc);
        Assert.Equal(1, job.NextCampaignOffset);
    }
}

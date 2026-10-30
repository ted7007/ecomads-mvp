using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbSyncOverviewTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastSuccess_UsesLatestEffectiveCompletionDate(bool latestHasNoCompletionDate)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateActiveDemoSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id,
            Marketplace = "Wildberries", Name = "WB", CreatedAt = DateTime.UtcNow };
        db.Sellers.Add(seller);
        db.Stores.Add(store);
        var old = new DateTime(2026, 9, 30, 3, 0, 0, DateTimeKind.Utc);
        var latest = old.AddMonths(1);
        foreach (var kind in new[] { "fullstats", "clusters", "jam" })
        {
            foreach (var (date, completed, status) in new[] {
                (old, (DateTime?)null, "completed"),
                (old.AddDays(1), (DateTime?)old.AddDays(1), "completed"),
                (latest, latestHasNoCompletionDate ? null : (DateTime?)latest, "completed"),
                (latest.AddDays(1), (DateTime?)latest.AddDays(1), "failed") })
            {
                db.WbSyncJobs.Add(new WbSyncJob { Id = Guid.NewGuid(), StoreId = store.Id,
                    Kind = kind, Status = status, CreatedAtUtc = date, UpdatedAtUtc = date,
                    CompletedAtUtc = completed, NextAttemptAtUtc = date,
                    StartDate = DateOnly.FromDateTime(date), EndDate = DateOnly.FromDateTime(date) });
            }
        }
        await db.SaveChangesAsync();
        var controller = new WbSyncVisibilityController(db) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, seller.Id.ToString())], "test")) } } };

        var result = Assert.IsType<OkObjectResult>(await controller.Overview(store.Id, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        foreach (var kind in new[] { "fullstats", "clusters", "jam" })
        {
            var source = Assert.Single(json.RootElement.GetProperty("sources").EnumerateArray(),
                x => x.GetProperty("kind").GetString() == kind);
            Assert.Equal(latest, source.GetProperty("lastSuccessAtUtc").GetDateTime());
        }
    }
}

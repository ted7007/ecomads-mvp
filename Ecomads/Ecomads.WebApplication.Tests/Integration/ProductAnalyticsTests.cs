using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class ProductAnalyticsTests
{
    private readonly PostgresFixture _postgres;

    public ProductAnalyticsTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task TrackAsyncSavesProductUsageEvent()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var service = new ProductAnalyticsService(dbContext, NullLogger<ProductAnalyticsService>.Instance);

        await service.TrackAsync(new ProductUsageEventCreateDto
        {
            UserId = seller.Id,
            EventName = ProductEvents.DashboardViewed,
            FeatureName = ProductFeatures.Dashboard,
            Metadata = new
            {
                campaignsCount = 2
            }
        });

        var productEvent = await dbContext.ProductUsageEvents.SingleAsync();

        Assert.Equal(seller.Id, productEvent.UserId);
        Assert.Equal(ProductEvents.DashboardViewed, productEvent.EventName);
        Assert.Equal(ProductFeatures.Dashboard, productEvent.FeatureName);
        Assert.Contains("\"campaignsCount\":2", productEvent.MetadataJson);
    }
}

using System.Net;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbProductBidsTests
{
    [Fact]
    public async Task MatchesMinimumByArticleAndPlacementAndKeepsKopecks()
    {
        var scenario = new WbHttpScenario()
            .Reply("""
                {"adverts":[{"advertId":123,"status":9,"currency":"RUB","settings":{"paymentType":"cpm","placements":["search"]},
                "nms":[{"nmId":1,"bids":[{"type":"search","value":40000}]}]}]}
                """)
            .Reply("""
                {"bids":[{"nm_id":1,"bids":[{"currency":"RUB","type":"search","value":35000},
                {"currency":"RUB","type":"recommendation","value":20000}]},
                {"nm_id":2,"bids":[{"currency":"RUB","type":"search","value":25000}]}]}
                """)
            .Reply("{}", HttpStatusCode.TooManyRequests, 900);
        using var http = new HttpClient(scenario) { BaseAddress = new Uri("https://wb.invalid") };
        var client = new WbPromotionClient(http);

        var campaign = Assert.Single(await client.GetProductBidsAsync("read-only", [123], default));
        Assert.Equal(new WbProductBid(1, "search", 40000), Assert.Single(campaign.Bids));
        var minimum = Assert.Single(await client.GetMinimumBidsAsync("read-only", 123, [1], "cpm", ["search"], default));
        Assert.Equal(new WbProductBid(1, "search", 35000), minimum);
        var rateLimit = await Assert.ThrowsAsync<WbApiException>(() =>
            client.GetMinimumBidsAsync("read-only", 123, [1], "cpm", ["search"], default));
        Assert.Equal(TimeSpan.FromMinutes(15), rateLimit.RetryAfter);
        Assert.Equal(new[] { "/api/advert/v3/adverts?advertIds=123", "/api/advert/v1/bids/min", "/api/advert/v1/bids/min" },
            scenario.Requests);
    }
}

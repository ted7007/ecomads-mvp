using System.Text.Json;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbFunnelSnapshotTests
{
    private static JsonDocument Page(IEnumerable<long> ids) => JsonSerializer.SerializeToDocument(new
    {
        data = new { products = ids.Select(id => new
        {
            product = new { nmId = id },
            statistic = new { selected = new { orderSum = 2m, orderCount = 1 }, past = new { orderSum = 3m, orderCount = 1 } }
        }).ToArray() }
    });

    [Fact]
    public void TerminalPageAddsEachProductOnceAndCarriesProvenance()
    {
        using var first = Page(Enumerable.Range(1, 1000).Select(x => (long)x));
        var page = WbSalesFunnelImporter.ReadProductsPage(first.RootElement, new());
        Assert.Equal(1000, page.State.Offset);
        Assert.Equal(2000, page.State.OrderSum);
        using var last = Page([1001, 1002]);
        var terminal = WbSalesFunnelImporter.ReadProductsPage(last.RootElement, page.State);
        Assert.Equal(2, terminal.ProductCount);
        Assert.Equal(2004, terminal.State.OrderSum);
        Assert.Equal(3006, terminal.State.PastOrderSum);
        Assert.Equal(1002, terminal.State.SeenProductIds!.Length);
        Assert.Equal(2000, page.State.OrderSum); // Original state remains safe to retry after a failure.
    }

    [Fact]
    public void RepeatedPageAndOverlappingProductCannotInflateTotals()
    {
        using var first = Page(Enumerable.Range(1, 1000).Select(x => (long)x));
        var page = WbSalesFunnelImporter.ReadProductsPage(first.RootElement, new());
        Assert.Throws<JsonException>(() => WbSalesFunnelImporter.ReadProductsPage(first.RootElement, page.State));
        using var overlap = Page([1001, 999]);
        Assert.Throws<JsonException>(() => WbSalesFunnelImporter.ReadProductsPage(overlap.RootElement, page.State));
        Assert.Equal(2000, page.State.OrderSum);
        Assert.Equal(1000, page.State.SeenProductIds!.Length);
    }

    [Theory]
    [InlineData("{\"data\":{\"products\":null}}")]
    [InlineData("{\"data\":{\"products\":[{\"product\":{\"nmId\":1},\"statistic\":{\"selected\":{},\"past\":{}}}]}}")]
    [InlineData("{\"data\":{\"products\":[{\"product\":{},\"statistic\":{}}]}}")]
    public void MissingDataDoesNotBecomeZero(string body)
    {
        using var page = JsonDocument.Parse(body);
        Assert.Throws<JsonException>(() => WbSalesFunnelImporter.ReadProductsPage(page.RootElement, new()));
    }

    [Fact]
    public void LegacyPartialChainRequiresRestart()
    {
        using var page = Page([1001]);
        Assert.Throws<JsonException>(() => WbSalesFunnelImporter.ReadProductsPage(page.RootElement,
            new(Offset: 1000, OrderSum: 100)));
    }
}

using System.Text.Json;
using Ecomads.WebApplication.Services.Recommendations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class OperationalRecommendationTests
{
    [Theory]
    [InlineData("2026-10-18T20:59:59Z", "2026-10-03", "2026-10-16")]
    [InlineData("2026-10-18T21:00:00Z", "2026-10-04", "2026-10-17")]
    public void WindowChangesAtMoscowMidnight(string utc, string start, string end)
    {
        var window = RecommendationPeriods.Economic(DateTime.Parse(utc).ToUniversalTime());
        Assert.Equal(14, window.Days);
        Assert.Equal(DateOnly.Parse(start), window.StartDate);
        Assert.Equal(DateOnly.Parse(end), window.EndDate);
    }

    [Theory]
    [InlineData(0, 14, "out_of_stock", 0)]
    [InlineData(2, 14, "stock_below_3_days", 1)]
    [InlineData(3, 14, "stock_below_7_days", 3)]
    [InlineData(6, 14, "stock_below_7_days", 3)]
    [InlineData(7, 14, "stock_sufficient", 4)]
    [InlineData(1, 0, "zero_demand", 4)]
    public void StockBoundariesUseUnitsAcrossFourteenDays(int quantity, int units, string reason, int priority)
    {
        var s = OperationalRules.Stock(quantity, 0, units, 30, true, true, "profit", false, true, false);
        Assert.Equal(reason, s.Reason); Assert.Equal(priority, s.Priority);
    }

    [Fact]
    public void MissingSourceAndZeroDemandAreNeverSubstitutedWithZero()
    {
        Assert.Equal("skipped", OperationalRules.Stock(0, null, 14, 30, true, true, "profit", false, true, false).Status);
        Assert.Equal("skipped", OperationalRules.Stock(0, 0, 14, 30, true, false, "profit", false, true, false).Status);
        var zero = OperationalRules.Stock(0, 0, null, 30, true, true, "profit", false, false, false);
        Assert.Equal("triggered", zero.Status); Assert.Equal("check_product", zero.Action.Kind);
        Assert.NotEqual("pause_campaign", zero.Action.Kind);
        Assert.Equal("pause_campaign", OperationalRules.Stock(0, 0, null, 30, true, true, "profit", true, false, false).Action.Kind);
        Assert.Null(OperationalRules.Stock(1, 1, 0, 30, true, true, "profit", false, true, false).StockDays);
    }

    [Fact]
    public void BothStockSchemesAreSummedAndClearanceNeverSuggestsSupply()
    {
        Assert.Equal(7m, OperationalRules.Stock(3, 4, 14, 30, true, true, "profit", false, true, false).StockDays);
        var clearance = OperationalRules.Stock(3, 1, 14, 30, true, true, "clearance", false, true, false);
        Assert.Equal("watch", clearance.Action.Kind); Assert.True(clearance.BlockGrowth);
        Assert.Equal("remove_article", OperationalRules.Stock(0, 0, 14, 30, true, true, "clearance", false, true, false).Action.Kind);
    }

    [Theory]
    [InlineData(100, "not_triggered", null)]
    [InlineData(99, "triggered", 201)]
    [InlineData(0, "triggered", 300)]
    public void BudgetUsesSevenDaysAndFillsToThreeDays(decimal budget, string status, int? topUp)
    {
        var s = OperationalRules.Budget(700, 3500, .2m, budget, 50, true, true, true);
        Assert.Equal(status, s.Status); Assert.Equal(topUp, s.TopUp);
        if (status == "triggered") Assert.True(s.InsufficientBalance);
    }

    [Fact]
    public void IneligibleCampaignNeverGetsTopUp()
    {
        Assert.Equal("not_triggered", OperationalRules.Budget(700, 3500, .2m, 0, 500, true, true, false).Status);
        Assert.Equal("not_triggered", OperationalRules.Budget(701, 3500, .2m, 0, 500, true, true, true).Status);
        Assert.Null(OperationalRules.Budget(0, 3500, .2m, 0, 500, true, true, true).Days);
        Assert.Equal("skipped", OperationalRules.Budget(700, 3500, .2m, 0, 500, false, true, true).Status);
        Assert.Equal("skipped", OperationalRules.Budget(700, 3500, .2m, null, 500, true, true, true).Status);
    }

    [Fact]
    public void ExactBidsNeedAllConstraintsAndNeverReduceBeyondThirtyPercent()
    {
        Assert.Equal(30, OperationalRules.Reduce(80, 100, null, 1, "article").ReductionPercent);
        Assert.Null(OperationalRules.Reduce(80, 100, null, 1, "article").TargetBid);
        Assert.Equal(75m, OperationalRules.Reduce(80, 100, 74, 5, "article").TargetBid);
        Assert.Equal("watch", OperationalRules.Reduce(30, 10, 10, 1, "article").Kind);
        Assert.Equal(71m, OperationalRules.Reduce(30, 101, 1, 1, "article").TargetBid);
    }

    [Fact]
    public void SnapshotFreshnessIncludesBoundaryAndRejectsFutureOrFailure()
    {
        var now = DateTime.UtcNow;
        var source = new SourceEvidence(Guid.NewGuid(), "stocks_fbs", "ready", now.AddHours(-1), now);
        Assert.True(source.Fresh(now));
        Assert.False((source with { RequestedAtUtc = now.AddHours(-1).AddTicks(-1) }).Fresh(now));
        Assert.False((source with { State = "source_error" }).Fresh(now));
        Assert.False((source with { ReceivedAtUtc = now.AddSeconds(1) }).Fresh(now));
    }

    [Fact]
    public void TransitIsIgnoredAndWarehouseAndSizeRemainDistinct()
    {
        using var doc = JsonDocument.Parse("""{"data":{"items":[{"nmId":1,"chrtId":2,"warehouseId":3,"quantity":4,"inWayToClient":100},{"nmId":1,"chrtId":5,"warehouseId":3,"quantity":6}]}}""");
        var positions = OperationalParsing.Stocks(doc.RootElement);
        Assert.Equal(10, positions.Sum(x => x.Quantity));
        Assert.Equal(2, positions.DistinctBy(x => (x.NmId, x.ChrtId, x.WarehouseId)).Count());
    }

    [Fact]
    public void BasicTokenReportUsesStockTotalWithoutTransitOrDoubleCounting()
    {
        using var doc = JsonDocument.Parse("""[{"nmId":1,"barcode":"123","techSize":"0","warehouses":[{"warehouseName":"To the client","quantity":14},{"warehouseName":"From the client","quantity":4},{"warehouseName":"Total in warehouses","quantity":7},{"warehouseName":"Warehouse WB Rus","quantity":7}]}]""");
        var position = Assert.Single(OperationalParsing.FbwReport(doc.RootElement));
        Assert.Equal(7, position.Quantity); Assert.Equal(4, position.Warehouses!.Count);
        Assert.Null(position.WarehouseId); Assert.Null(position.ChrtId);
        using var unknown = JsonDocument.Parse(doc.RootElement.GetRawText().Replace("Total in warehouses", "Unknown total"));
        Assert.Throws<JsonException>(() => OperationalParsing.FbwReport(unknown.RootElement));
        using var duplicate = JsonDocument.Parse("[" + doc.RootElement[0].GetRawText() + "," + doc.RootElement[0].GetRawText() + "]");
        Assert.Throws<JsonException>(() => OperationalParsing.FbwReport(duplicate.RootElement));
    }
}

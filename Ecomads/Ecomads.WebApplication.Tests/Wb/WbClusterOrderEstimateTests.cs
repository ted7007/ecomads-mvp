using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbClusterOrderEstimateTests
{
    private static readonly DateOnly Start = new(2026, 7, 1);
    private static readonly DateOnly End = new(2026, 7, 2);
    private static CampaignNomenclatureStatistics Article(DateOnly date, decimal sum, int? units) =>
        new() { Date = date.ToDateTime(TimeOnly.MinValue), Revenue = sum, OrderedProducts = units, Orders = 999 };
    private static WbClusterStatistic Cluster(DateOnly date, decimal spend, int? units) =>
        new() { Date = date, Spend = spend, OrderedProducts = units, Orders = 999 };

    [Fact]
    public void UsesWeightedUnitPriceOnlyInsideSelectedPeriodAndShksInsteadOfOrders()
    {
        var result = WbClusterOrderEstimateCalculator.Calculate(Start, End,
            [Article(Start.AddDays(-1), 999999, 1), Article(Start, 100, 1), Article(End, 900, 3),
             Article(End.AddDays(1), 999999, 1)],
            [Cluster(Start.AddDays(-1), 9999, 999), Cluster(Start, 50, 1), Cluster(End, 75, 1),
             Cluster(End.AddDays(1), 9999, 999)], true, true, false);
        Assert.Equal(250m, result.AverageOrderedUnitPrice);
        Assert.Equal(500m, result.EstimatedOrderSum);
        Assert.Equal(25m, result.EstimatedDrr);
        Assert.Equal("complete", result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void MissingOrInvalidFullstatsUnitsDoNotFallBackToOrders(int? units)
    {
        var result = WbClusterOrderEstimateCalculator.Calculate(Start, End,
            [Article(Start, 100, units)], [Cluster(Start, 10, 1)], true, true, false);
        Assert.Null(result.AverageOrderedUnitPrice);
        Assert.Null(result.EstimatedOrderSum);
        Assert.Null(result.EstimatedDrr);
        Assert.Equal("unavailable", result.Status);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(100, 0)]
    public void ZeroEstimatedSumHasNoDrr(decimal sum, int clusterUnits)
    {
        var result = WbClusterOrderEstimateCalculator.Calculate(Start, End,
            [Article(Start, sum, 2)], [Cluster(Start, 10, clusterUnits)], true, true, false);
        Assert.Equal(0m, result.EstimatedOrderSum);
        Assert.Null(result.EstimatedDrr);
    }

    [Fact]
    public void MissingAnyClusterUnitsMakesSumUnknown()
    {
        var result = WbClusterOrderEstimateCalculator.Calculate(Start, End,
            [Article(Start, 100, 2)], [Cluster(Start, 10, 1), Cluster(End, 10, null)], true, true, false);
        Assert.Equal(50m, result.AverageOrderedUnitPrice);
        Assert.Null(result.EstimatedOrderSum);
        Assert.Equal("unavailable", result.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void PartialClustersOrCurrentDayAreExplicitlyPreliminary(bool complete, bool today)
    {
        var result = WbClusterOrderEstimateCalculator.Calculate(Start, End,
            [Article(Start, 100, 2)], [Cluster(Start, 10, 1)], true, complete, today);
        Assert.Equal(50m, result.EstimatedOrderSum);
        Assert.Equal("preliminary", result.Status);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void IncompleteFullstatsCannotSupplyAnAverageForTheWholePeriod()
    {
        var result = WbClusterOrderEstimateCalculator.Calculate(Start, End,
            [Article(Start, 100, 2)], [Cluster(Start, 10, 1)], false, true, false);
        Assert.Null(result.EstimatedOrderSum);
        Assert.Equal("unavailable", result.Status);
    }
}

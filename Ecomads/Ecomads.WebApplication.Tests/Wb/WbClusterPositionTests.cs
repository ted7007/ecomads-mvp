using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbClusterPositionTests
{
    private static readonly DateOnly Start = new(2026, 10, 12);
    private static WbClusterStatistic Row(int offset, decimal? position) => new() { Date = Start.AddDays(offset), AveragePosition = position };

    [Fact]
    public void AveragesOnlySelectedDailyPositionsAndReportsItsBasis()
    {
        var result = WbClusterPositionCalculator.Calculate(Start, Start.AddDays(1),
            [Row(-1, 999), Row(0, 4), Row(1, 20), Row(2, 999)], true, false);
        Assert.Equal(12m, result.AveragePosition);
        Assert.Equal(2, result.Days);
        Assert.Equal("complete", result.Status);
        Assert.Contains("арифметическое", result.Reason);
    }

    [Fact]
    public void MissingZeroAndNegativePositionsNeverBecomeRealZeroPositions()
    {
        var result = WbClusterPositionCalculator.Calculate(Start, Start.AddDays(3),
            [Row(0, null), Row(1, 0), Row(2, -1), Row(3, 8)], true, false);
        Assert.Equal(8m, result.AveragePosition);
        Assert.Equal(1, result.Days);
        Assert.Equal("preliminary", result.Status);
        Assert.Null(WbClusterPositionCalculator.Calculate(Start, Start, [Row(0, 0)], true, false).AveragePosition);
        Assert.Null(WbClusterPositionCalculator.Calculate(Start, Start, [], true, false).AveragePosition);
    }

    [Fact]
    public void RequiresExplicitDatesAndDoesNotClaimPartialOrTodayAsComplete()
    {
        Assert.Null(WbClusterPositionCalculator.Calculate(null, null, [Row(0, 7)], true, false).AveragePosition);
        Assert.Null(WbClusterPositionCalculator.Calculate(Start.AddDays(1), Start, [Row(0, 7)], true, false).AveragePosition);
        Assert.Equal("preliminary", WbClusterPositionCalculator.Calculate(Start, Start, [Row(0, 7)], false, false).Status);
        Assert.Equal("preliminary", WbClusterPositionCalculator.Calculate(Start, Start, [Row(0, 7)], true, true).Status);
    }
}

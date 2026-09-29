using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbSpendTrendTests
{
    [Fact]
    public void DetectsIncreaseOnlyWithCompleteEightDayCoverage()
    {
        var end = new DateOnly(2026, 7, 8);
        var rows = Enumerable.Range(1, 7).Select(day => Row(day, 100m)).ToList();
        rows.Add(Row(8, 150m));

        var complete = WbSpendTrendCalculator.Calculate(end, rows, 40m, 2, 3);
        Assert.Equal("increase", complete.Status);
        Assert.Equal(50m, complete.ChangePercent);
        Assert.Equal(8, complete.LoadedDays);
        Assert.Equal(2, complete.StoreNormVersion);
        Assert.Equal(3, complete.CampaignNormVersion);

        rows.RemoveAt(0);
        var incomplete = WbSpendTrendCalculator.Calculate(end, rows, 40m, 2, 3);
        Assert.Equal("insufficient", incomplete.Status);
        Assert.Null(incomplete.ChangePercent);
    }

    private static CampaignStatistics Row(int day, decimal spend) => new()
    {
        CampaignId = Guid.Empty,
        Date = new DateTime(2026, 7, day, 0, 0, 0, DateTimeKind.Utc),
        Spend = spend
    };
}

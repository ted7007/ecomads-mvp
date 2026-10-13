using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbRefreshClockTests
{
    [Theory]
    [InlineData("2026-10-06T21:14:00Z", false, "2026-10-06T15:15:00Z")]
    [InlineData("2026-10-07T03:14:00Z", false, "2026-10-06T15:15:00Z")]
    [InlineData("2026-10-07T03:15:00Z", false, "2026-10-07T03:15:00Z")]
    [InlineData("2026-10-07T15:14:00Z", false, "2026-10-07T03:15:00Z")]
    [InlineData("2026-10-07T15:15:00Z", false, "2026-10-07T15:15:00Z")]
    [InlineData("2026-10-07T03:14:00Z", true, "2026-10-06T03:15:00Z")]
    [InlineData("2026-10-07T03:15:00Z", true, "2026-10-07T03:15:00Z")]
    public void SlotsUseMoscowAndCatchUpOnlyLatest(string now, bool daily, string expected)
    {
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(),
            WbRefreshClock.LatestSlot(DateTime.Parse(now).ToUniversalTime(), daily));
    }

    [Fact]
    public void OnlyTwoOperationalSlotsPerMoscowDay()
    {
        var start = new DateTime(2026, 10, 6, 21, 0, 0, DateTimeKind.Utc);
        var slots = Enumerable.Range(0, 24 * 60).Select(i => WbRefreshClock.NextSlot(start.AddMinutes(i)))
            .Where(x => x >= start && x < start.AddDays(1)).Distinct().ToArray();
        Assert.Equal(new[] { start.AddHours(6).AddMinutes(15), start.AddHours(18).AddMinutes(15) }, slots);
    }
}

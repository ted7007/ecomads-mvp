using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbRefreshClockTests
{
    [Theory]
    [InlineData("2026-10-06T21:14:00Z", false, "2026-10-06T19:15:00Z")]
    [InlineData("2026-10-06T21:15:00Z", false, "2026-10-06T21:15:00Z")]
    [InlineData("2026-10-07T03:14:00Z", true, "2026-10-06T03:15:00Z")]
    [InlineData("2026-10-07T03:15:00Z", true, "2026-10-07T03:15:00Z")]
    public void SlotsUseMoscowAndCatchUpOnlyLatest(string now, bool daily, string expected)
    {
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(),
            WbRefreshClock.LatestSlot(DateTime.Parse(now).ToUniversalTime(), daily));
    }
}

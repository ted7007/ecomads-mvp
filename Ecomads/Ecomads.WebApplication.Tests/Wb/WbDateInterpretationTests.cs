using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbDateInterpretationTests
{
    [Theory]
    [InlineData("2026-10-13T22:30:00Z", "2026-10-14")]
    [InlineData("2026-10-14T01:30:00+03:00", "2026-10-14")]
    [InlineData("2026-10-14T08:30:00+10:00", "2026-10-14")]
    [InlineData("2026-12-31T20:59:59Z", "2026-12-31")]
    [InlineData("2026-12-31T21:00:00Z", "2027-01-01")]
    [InlineData("2024-02-28T21:00:00Z", "2024-02-29")]
    [InlineData("2024-02-29T21:00:00Z", "2024-03-01")]
    public void InstantUsesMoscowCalendar(string raw, string day)
    {
        Assert.True(WbDates.TryInstant(raw, out var instant));
        Assert.Equal(DateTimeKind.Utc, instant.Kind);
        Assert.Equal(DateOnly.Parse(day), WbDates.MoscowDay(instant));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("2026-10-13")]
    [InlineData("2026-10-13T22:30:00")] [InlineData("2026-02-29T22:30:00Z")]
    [InlineData("22:30:00Z")] [InlineData("October 13 2026 22:30:00Z")]
    public void DoesNotGuessMissingOffset(string? raw) => Assert.False(WbDates.TryInstant(raw, out _));

    [Fact]
    public void DailyReportDateIsCalendarLabel() => Assert.Equal(new DateOnly(2026, 10, 13),
        WbDates.CalendarDay("2026-10-13T22:30:00Z"));
}

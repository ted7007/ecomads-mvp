using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbExpenseWindowTests
{
    [Theory]
    [InlineData("2026-01-01", "2026-01-29", "2026-02-01T00:00:00Z", "2025-12-31", "2026-01-30")]
    [InlineData("2024-02-01", "2024-02-29", "2024-03-01T00:00:00Z", "2024-01-31", "2024-03-01")]
    [InlineData("2026-10-13", "2026-10-14", "2026-10-13T21:00:00Z", "2026-10-12", "2026-10-14")]
    public void PadsCalendarDaysAndCapsAtMoscowToday(string start, string end, string now, string left, string right)
    {
        var window = WbExpenseWindow.Create(DateOnly.Parse(start), DateOnly.Parse(end), DateTimeOffset.Parse(now).UtcDateTime);
        Assert.Equal(DateOnly.Parse(left), window.RequestStart);
        Assert.Equal(DateOnly.Parse(right), window.RequestEnd);
        Assert.InRange(window.RequestEnd.DayNumber - window.RequestStart.DayNumber + 1, 1, 31);
    }

    [Theory]
    [InlineData(29)] [InlineData(30)] [InlineData(31)] [InlineData(365)] [InlineData(366)]
    public void PartsCoverEveryTargetDayExactlyOnce(int days)
    {
        var start = new DateOnly(2024, 1, 1); var end = start.AddDays(days - 1);
        var parts = WbSyncPlanner.ExpenseHistoryBatches(start, end, new HashSet<DateOnly>());
        var covered = parts.SelectMany(part => Enumerable.Range(0, part.EndDate.DayNumber - part.StartDate.DayNumber + 1)
            .Select(i => part.StartDate.AddDays(i))).ToArray();
        Assert.Equal(days, covered.Length); Assert.Equal(days, covered.Distinct().Count());
        Assert.Equal(start, covered.Min()); Assert.Equal(end, covered.Max());
        Assert.All(parts, part => Assert.InRange(part.EndDate.DayNumber - part.StartDate.DayNumber + 1, 1, 29));
        Assert.All(parts, part => Assert.InRange(WbExpenseWindow.Create(part.StartDate, part.EndDate,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).RequestEnd.DayNumber - part.StartDate.DayNumber + 2, 1, 31));
    }
}

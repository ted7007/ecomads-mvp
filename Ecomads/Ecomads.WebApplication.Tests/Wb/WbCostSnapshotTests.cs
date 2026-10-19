using System.Globalization;
using System.Text.Json;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbCostSnapshotTests
{
    [Theory]
    [InlineData(29)] [InlineData(30)] [InlineData(31)] [InlineData(365)] [InlineData(366)]
    public void PartitionAndRowOrderDoNotChangeTotalsAndIdenticalOperationsKeepMultiplicity(int days)
    {
        var start = new DateOnly(2024, 2, 1); var end = start.AddDays(days - 1);
        var rows = Enumerable.Range(-1, days + 2).SelectMany(i => Enumerable.Range(0, 2).Select(_ => new {
            Date = start.AddDays(i), Operation = new { updNum = 1,
                updTime = start.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:30:00+03:00",
                updSum = (i + 2) / 4m } })).ToArray();
        using var full = JsonSerializer.SerializeToDocument(rows.Select(x => x.Operation));
        var parsed = WbCostSnapshot.Parse(full.RootElement, start, end);
        Assert.Equal(rows.Length, parsed.Rows.Count);
        Assert.All(parsed.Rows, x => Assert.True(x.State is "data" or "adjacent"));
        Assert.All(parsed.Counts, x => Assert.Equal(2, x.Value));
        var fullTotals = parsed.Totals.Where(x => x.Key >= start && x.Key <= end).OrderBy(x => x.Key).ToArray();
        var parts = new Dictionary<DateOnly, decimal>();
        foreach (var part in WbSyncPlanner.ExpenseHistoryBatches(start, end, new HashSet<DateOnly>()))
        {
            using var response = JsonSerializer.SerializeToDocument(rows.Where(x => x.Date >= part.StartDate.AddDays(-1) &&
                x.Date <= part.EndDate.AddDays(1)).Select(x => x.Operation));
            var snapshot = WbCostSnapshot.Parse(response.RootElement, part.StartDate, part.EndDate);
            foreach (var total in snapshot.Totals.Where(x => x.Key >= part.StartDate && x.Key <= part.EndDate))
                Assert.True(parts.TryAdd(total.Key, total.Value));
        }
        Assert.Equal(fullTotals, parts.OrderBy(x => x.Key).ToArray());
        using var reordered = JsonSerializer.SerializeToDocument(rows.Reverse().Select(x => x.Operation));
        Assert.Equal(fullTotals, WbCostSnapshot.Parse(reordered.RootElement, start, end).Totals
            .Where(x => x.Key >= start && x.Key <= end).OrderBy(x => x.Key).ToArray());
        Assert.Equal(1m, parsed.Totals[start]); // Two identical 0.50 operations, both with updNum=1.
    }

    [Fact]
    public void InvalidAmountAffectsItsDayAndEveryRowRetainsInterpretation()
    {
        using var response = JsonDocument.Parse("""
            [{"updTime":"2026-10-13T10:00:00+03:00","updSum":10},
             {"updTime":"2026-10-14T10:00:00+03:00"}]
            """);
        var day = new DateOnly(2026, 10, 13);
        var snapshot = WbCostSnapshot.Parse(response.RootElement, day, day.AddDays(1));
        Assert.False(snapshot.AmbiguousPeriod);
        Assert.Equal(10, snapshot.Totals[day]);
        Assert.DoesNotContain(day, snapshot.BadDates);
        Assert.Contains(day.AddDays(1), snapshot.BadDates);
        Assert.Equal(new[] { "data", "invalid_amount" }, snapshot.Rows.Select(x => x.State));
        Assert.All(snapshot.Rows, x => Assert.NotEmpty(x.RawJson));
        Assert.Equal("2026-10-14T10:00:00+03:00", snapshot.Rows[1].RawTime);
        Assert.Null(snapshot.Rows[1].Amount);
    }
}

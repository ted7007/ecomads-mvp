using System.Globalization;
using System.Text.RegularExpressions;
using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

// Mutable holder flows into HttpClient's handler scope; never holds credentials.
public sealed class WbIngestionContext : IDisposable
{
    private static readonly AsyncLocal<WbIngestionContext?> Slot = new();
    private readonly WbIngestionContext? previous;
    public static WbIngestionContext? Current => Slot.Value;
    public WbSyncJob Job { get; }
    public Guid? SnapshotId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public bool HasIssues { get; set; }
    public List<string> Issues { get; } = [];
    public void Reject(string reason) { HasIssues = true; Issues.Add(reason); }
    public WbIngestionContext(WbSyncJob job, DateTime now)
    {
        previous = Slot.Value; Slot.Value = this; Job = job;
        RequestedAtUtc = ReceivedAtUtc = now;
    }
    public void Dispose() => Slot.Value = previous;
}

public static partial class WbDates
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetSuffix();
    public static bool TryInstant(string? text, out DateTime utc)
    {
        utc = default;
        if (text == null || !OffsetSuffix().IsMatch(text) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        utc = parsed.UtcDateTime; return true;
    }
    public static DateOnly MoscowDay(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, WbRefreshClock.Moscow));
    public static DateOnly CalendarDay(string? text)
    {
        if (text == null || text.Length < 10 || (text.Length > 10 && text[10] != 'T') ||
            !DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new System.Text.Json.JsonException("Некорректная календарная дата WB.");
        if (text.Length > 10 && !TryInstant(text, out _) &&
            !DateTime.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new System.Text.Json.JsonException("Некорректная календарная дата WB.");
        return day;
    }
}

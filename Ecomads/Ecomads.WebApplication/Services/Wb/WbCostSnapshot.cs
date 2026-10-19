using System.Text.Json;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbParsedCost(int Ordinal, string RawJson, string? RawTime, DateTime? OccurredAtUtc,
    DateOnly? MoscowDate, decimal? Amount, string State);
public sealed record WbParsedCostSnapshot(IReadOnlyList<WbParsedCost> Rows,
    IReadOnlyDictionary<DateOnly, decimal> Totals, IReadOnlyDictionary<DateOnly, int> Counts,
    IReadOnlySet<DateOnly> BadDates, bool AmbiguousPeriod);

public static class WbCostSnapshot
{
    public static WbParsedCostSnapshot Parse(JsonElement root, DateOnly start, DateOnly end)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("История затрат WB не является массивом.");
        var rows = new List<WbParsedCost>();
        var totals = new Dictionary<DateOnly, decimal>(); var counts = new Dictionary<DateOnly, int>();
        var badDates = new HashSet<DateOnly>(); var ambiguous = false;
        foreach (var item in root.EnumerateArray())
        {
            var rawTime = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("updTime", out var t) &&
                t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            DateTime? instant = WbDates.TryInstant(rawTime, out var parsed) ? parsed : null;
            DateOnly? date = instant.HasValue ? WbDates.MoscowDay(instant.Value) : null;
            decimal? amount = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("updSum", out var a) &&
                a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out var money) && money >= 0 ? money : null;
            var state = date == null ? "ambiguous_time" : amount == null ? "invalid_amount" :
                date < start.AddDays(-2) || date > end.AddDays(2) ? "unexpected_date" :
                date < start || date > end ? "adjacent" : "data";
            rows.Add(new(rows.Count, item.GetRawText(), rawTime, instant, date, amount, state));
            if (date == null) { ambiguous = true; continue; }
            if (state is "invalid_amount" or "unexpected_date")
            { badDates.Add(date.Value); ambiguous |= state == "unexpected_date"; continue; }
            totals[date.Value] = totals.GetValueOrDefault(date.Value) + amount!.Value;
            counts[date.Value] = counts.GetValueOrDefault(date.Value) + 1;
        }
        return new(rows, totals, counts, badDates, ambiguous);
    }
}

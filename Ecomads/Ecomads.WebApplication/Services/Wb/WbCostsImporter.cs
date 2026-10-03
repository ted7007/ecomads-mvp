using System.Globalization;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbCostsImporter(EcomadsDbContext db)
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    public async Task<int> ImportAsync(Guid storeId, DateOnly start, DateOnly end, JsonElement root,
        CancellationToken ct)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("История затрат WB не является массивом.");
        var totals = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1)
            .ToDictionary(offset => start.AddDays(offset), _ => 0m);
        var count = 0;
        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("updSum", out var amount) || amount.ValueKind != JsonValueKind.Number ||
                !amount.TryGetDecimal(out var spend) || spend < 0)
                throw new JsonException("История затрат WB содержит некорректную сумму.");
            if (!item.TryGetProperty("updTime", out var timestamp) || timestamp.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time))
                throw new JsonException("WB не указал дату списания; дневной расход нельзя подтвердить.");
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, Moscow).DateTime);
            if (!totals.ContainsKey(date))
                throw new JsonException("WB вернул списание за пределами запрошенного периода.");
            totals[date] += spend;
            count++;
        }
        var existing = await db.WbStoreDailySpends.Where(x => x.StoreId == storeId && x.Date >= start && x.Date <= end)
            .ToDictionaryAsync(x => x.Date, ct);
        var loadedAt = DateTime.UtcNow;
        foreach (var (date, spend) in totals)
        {
            if (!existing.TryGetValue(date, out var row))
            {
                row = new WbStoreDailySpend { StoreId = storeId, Date = date };
                db.WbStoreDailySpends.Add(row);
            }
            row.Spend = spend;
            row.LoadedAtUtc = loadedAt;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }
}

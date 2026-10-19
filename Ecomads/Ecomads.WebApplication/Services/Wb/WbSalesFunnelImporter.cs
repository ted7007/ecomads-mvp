using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbFunnelPageState(int Offset = 0, decimal OrderSum = 0m, int OrderCount = 0,
    decimal PastOrderSum = 0m, int PastOrderCount = 0, long[]? SeenProductIds = null, Guid[]? SnapshotIds = null,
    DateTime? StartedAtUtc = null);

public sealed class WbSalesFunnelImporter(EcomadsDbContext db, ILogger<WbSalesFunnelImporter> logger)
{
    public static (WbFunnelPageState State, int ProductCount) ReadProductsPage(JsonElement root, WbFunnelPageState previous)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            throw new JsonException("Воронка WB не содержит товары.");
        if (products.GetArrayLength() > 1000) throw new JsonException("Воронка WB превысила размер запрошенной страницы.");
        if (previous.Offset > 0 && previous.SeenProductIds == null)
            throw new JsonException("Старая цепочка страниц не содержит сведений о товарах; требуется начать её заново.");
        var ids = (previous.SeenProductIds ?? []).ToHashSet();
        var selectedSum = previous.OrderSum; var selectedCount = previous.OrderCount;
        var pastSum = previous.PastOrderSum; var pastCount = previous.PastOrderCount;
        foreach (var product in products.EnumerateArray())
        {
            if (product.ValueKind != JsonValueKind.Object) throw new JsonException("Некорректный товар воронки WB.");
            var descriptor = product.TryGetProperty("product", out var nested) ? nested : product;
            if (descriptor.ValueKind != JsonValueKind.Object || !descriptor.TryGetProperty("nmId", out var id) ||
                id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var nmId) || nmId <= 0 || !ids.Add(nmId))
                throw new JsonException("Повторный товар или отсутствующий nmId в страницах воронки.");
            if (!product.TryGetProperty("statistic", out var statistic) || statistic.ValueKind != JsonValueKind.Object ||
                !statistic.TryGetProperty("selected", out var selected) || selected.ValueKind != JsonValueKind.Object ||
                !statistic.TryGetProperty("past", out var past) || past.ValueKind != JsonValueKind.Object)
                throw new JsonException("Нет обязательных периодов товарной статистики WB.");
            selectedSum += Money(selected, "orderSum"); selectedCount = checked(selectedCount + Number(selected, "orderCount"));
            pastSum += Money(past, "orderSum"); pastCount = checked(pastCount + Number(past, "orderCount"));
        }
        var snapshots = (previous.SnapshotIds ?? []).ToList();
        if (WbIngestionContext.Current?.SnapshotId is { } snapshotId) snapshots.Add(snapshotId);
        return (new(products.GetArrayLength() == 1000 ? previous.Offset + 1000 : previous.Offset,
            selectedSum, selectedCount, pastSum, pastCount, ids.ToArray(), snapshots.ToArray(),
            previous.StartedAtUtc ?? WbIngestionContext.Current?.RequestedAtUtc), products.GetArrayLength());
    }

    public async Task ImportProductsAsync(Guid storeId, DateOnly day, DateOnly pastDay, WbFunnelPageState totals,
        CancellationToken ct, bool replaceExisting = false, DateTime? snapshotAtUtc = null)
    {
        var at = snapshotAtUtc ?? WbIngestionContext.Current?.RequestedAtUtc ?? DateTime.UtcNow;
        var quality = new WbQuality(db);
        foreach (var date in new[] { day, pastDay }.Distinct())
        {
            var old = await db.WbStoreDailyOrders.FindAsync([storeId, date], ct);
            if (old != null && (!replaceExisting || old.LoadedAtUtc > at)) continue;
            var sum = date == day ? totals.OrderSum : totals.PastOrderSum;
            var count = date == day ? totals.OrderCount : totals.PastOrderCount;
            var state = totals.SeenProductIds is { Length: 0 } ? "no_data" : sum == 0 && count == 0 ? "zero" : "data";
            if (!await quality.ObserveAsync(storeId, "orders", "", date, date, state, ct,
                previous: old?.OrderSum, proposed: sum, observedAtUtc: at)) continue;
            if (!WbQuality.Confirmed(state)) continue;
            if (old == null) { old = new() { StoreId = storeId, Date = date }; db.WbStoreDailyOrders.Add(old); }
            old.OrderSum = sum; old.OrderCount = count; old.Source = "products"; old.LoadedAtUtc = at;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ImportAsync(Guid storeId, DateOnly start, DateOnly end, JsonElement root, CancellationToken ct)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new JsonException("Воронка WB имеет неожиданный формат.");
        var totals = new Dictionary<DateOnly, WbStoreDailyOrders>();
        var groupCounts = new Dictionary<DateOnly, int>();
        var invalid = new HashSet<DateOnly>();
        var invalidGroup = false;
        foreach (var group in data.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object || (group.TryGetProperty("currency", out var currency) &&
                (currency.ValueKind != JsonValueKind.String || currency.GetString() != "RUB")) ||
                !group.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array)
            { invalidGroup = true; continue; }
            var seen = new HashSet<DateOnly>();
            foreach (var day in history.EnumerateArray())
            {
                DateOnly date;
                try { date = WbDates.CalendarDay(day.GetProperty("date").GetString()); }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
                { invalidGroup = true; continue; }
                if (date < start || date > end) { invalidGroup = true; continue; }
                if (!seen.Add(date)) { invalid.Add(date); continue; }
                try
                {
                    var orders = Number(day, "orderCount"); var sum = Money(day, "orderSum");
                    var opens = Number(day, "openCount"); var carts = Number(day, "cartCount");
                    var buyouts = Number(day, "buyoutCount"); var buyoutSum = Money(day, "buyoutSum");
                    if (!totals.TryGetValue(date, out var row)) totals[date] = row = new() { StoreId = storeId, Date = date };
                    checked { row.OrderCount += orders; row.OrderSum += sum; row.OpenCount += opens; row.CartCount += carts;
                        row.BuyoutCount += buyouts; row.BuyoutSum += buyoutSum; }
                    groupCounts[date] = groupCounts.GetValueOrDefault(date) + 1;
                }
                catch (Exception error) when (error is JsonException or OverflowException) { invalid.Add(date); }
            }
        }
        var quality = new WbQuality(db);
        var at = WbIngestionContext.Current?.RequestedAtUtc ?? DateTime.UtcNow;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            totals.TryGetValue(date, out var total);
            var row = await db.WbStoreDailyOrders.FindAsync([storeId, date], ct);
            var state = invalidGroup || invalid.Contains(date) ? "ambiguous" :
                total == null || groupCounts.GetValueOrDefault(date) != data.GetArrayLength() ? "no_data" :
                total.OrderSum == 0 && total.OrderCount == 0 ? "zero" : "data";
            if (!WbQuality.Confirmed(state)) logger.LogInformation("WB order day is {State} for store {StoreId}, date {Date}", state, storeId, date);
            if (!await quality.ObserveAsync(storeId, "orders", "", date, date, state, ct,
                WbQuality.Confirmed(state) ? null : "WB не подтвердил все обязательные показатели дня.",
                previous: row?.OrderSum, proposed: total?.OrderSum)) continue;
            if (!WbQuality.Confirmed(state)) continue;
            if (row == null) { row = new() { StoreId = storeId, Date = date }; db.WbStoreDailyOrders.Add(row); }
            row.OrderCount = total!.OrderCount; row.OrderSum = total.OrderSum; row.OpenCount = total.OpenCount;
            row.CartCount = total.CartCount; row.BuyoutCount = total.BuyoutCount; row.BuyoutSum = total.BuyoutSum;
            row.Source = "history"; row.LoadedAtUtc = at;
        }
        await db.SaveChangesAsync(ct);
    }
    private static int Number(JsonElement row, string key) => row.TryGetProperty(key, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= 0 ? n : throw new JsonException($"Нет корректного поля {key}.");
    private static decimal Money(JsonElement row, string key) => row.TryGetProperty(key, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) && n >= 0 ? n : throw new JsonException($"Нет корректного поля {key}.");
}

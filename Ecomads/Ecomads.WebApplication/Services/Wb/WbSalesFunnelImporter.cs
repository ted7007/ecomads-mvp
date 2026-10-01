using System.Globalization;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbFunnelPageState(int Offset = 0, decimal OrderSum = 0m, int OrderCount = 0,
    decimal PastOrderSum = 0m, int PastOrderCount = 0);

public sealed class WbSalesFunnelImporter(EcomadsDbContext db, ILogger<WbSalesFunnelImporter> logger)
{
    public static (WbFunnelPageState State, int ProductCount) ReadProductsPage(JsonElement root,
        WbFunnelPageState previous)
    {
        var products = root.GetProperty("data").GetProperty("products");
        if (products.ValueKind != JsonValueKind.Array) throw new JsonException("Воронка WB не содержит товары.");
        var selectedSum = previous.OrderSum;
        var selectedCount = previous.OrderCount;
        var pastSum = previous.PastOrderSum;
        var pastCount = previous.PastOrderCount;
        foreach (var product in products.EnumerateArray())
        {
            var statistic = product.GetProperty("statistic");
            var selected = statistic.GetProperty("selected");
            var past = statistic.GetProperty("past");
            selectedSum += Money(selected, "orderSum");
            selectedCount += Number(selected, "orderCount");
            pastSum += Money(past, "orderSum");
            pastCount += Number(past, "orderCount");
        }
        return (new WbFunnelPageState(products.GetArrayLength() == 1000
            ? previous.Offset + 1000 : previous.Offset, selectedSum, selectedCount, pastSum, pastCount),
            products.GetArrayLength());
    }

    public async Task ImportProductsAsync(Guid storeId, DateOnly day, DateOnly pastDay,
        WbFunnelPageState totals, CancellationToken cancellationToken)
    {
        var dates = new[] { day, pastDay };
        var existing = await db.WbStoreDailyOrders.Where(x => x.StoreId == storeId && dates.Contains(x.Date))
            .Select(x => x.Date).ToListAsync(cancellationToken);
        var loadedAt = DateTime.UtcNow;
        if (!existing.Contains(day)) db.WbStoreDailyOrders.Add(new WbStoreDailyOrders
        {
            StoreId = storeId, Date = day, OrderCount = totals.OrderCount, OrderSum = totals.OrderSum,
            Source = "products", LoadedAtUtc = loadedAt
        });
        if (!existing.Contains(pastDay)) db.WbStoreDailyOrders.Add(new WbStoreDailyOrders
        {
            StoreId = storeId, Date = pastDay, OrderCount = totals.PastOrderCount, OrderSum = totals.PastOrderSum,
            Source = "products", LoadedAtUtc = loadedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ImportAsync(Guid storeId, DateOnly start, DateOnly end, JsonElement root,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new JsonException("Воронка WB имеет неожиданный формат.");
        var totals = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1)
            .ToDictionary(offset => start.AddDays(offset), offset => new WbStoreDailyOrders
            { StoreId = storeId, Date = start.AddDays(offset) });
        foreach (var group in data.EnumerateArray())
        {
            if (group.TryGetProperty("currency", out var currency) && currency.GetString() != "RUB")
            {
                logger.LogWarning("Skipped non-RUB sales funnel group for store {StoreId}", storeId);
                continue;
            }
            if (!group.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array)
                throw new JsonException("Воронка WB не содержит дневную историю.");
            foreach (var day in history.EnumerateArray())
            {
                var dateText = day.GetProperty("date").GetString();
                if (dateText == null || dateText.Length < 10 ||
                    !DateOnly.TryParseExact(dateText[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                    throw new JsonException("Воронка WB содержит некорректную дату.");
                if (!totals.TryGetValue(date, out var row)) continue;
                row.OrderCount += Number(day, "orderCount");
                row.OrderSum += Money(day, "orderSum");
                row.OpenCount += Number(day, "openCount");
                row.CartCount += Number(day, "cartCount");
                row.BuyoutCount += Number(day, "buyoutCount");
                row.BuyoutSum += Money(day, "buyoutSum");
            }
        }

        var existing = await db.WbStoreDailyOrders.Where(x => x.StoreId == storeId && x.Date >= start && x.Date <= end)
            .ToDictionaryAsync(x => x.Date, cancellationToken);
        var loadedAt = DateTime.UtcNow;
        foreach (var (date, total) in totals)
        {
            if (!existing.TryGetValue(date, out var row))
            {
                row = total;
                db.WbStoreDailyOrders.Add(row);
            }
            row.OrderCount = total.OrderCount;
            row.OrderSum = total.OrderSum;
            row.OpenCount = total.OpenCount;
            row.CartCount = total.CartCount;
            row.BuyoutCount = total.BuyoutCount;
            row.BuyoutSum = total.BuyoutSum;
            row.Source = "history";
            row.LoadedAtUtc = loadedAt;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static int Number(JsonElement day, string property) =>
        day.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static decimal Money(JsonElement day, string property) =>
        day.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0m;
}

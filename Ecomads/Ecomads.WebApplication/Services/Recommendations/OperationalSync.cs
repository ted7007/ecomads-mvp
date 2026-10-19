using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record WarehouseRemain(string WarehouseName, decimal Quantity);
public sealed record StockPosition(long NmId, long? ChrtId, long? WarehouseId, decimal Quantity,
    string? Barcode = null, string? Size = null, IReadOnlyList<WarehouseRemain>? Warehouses = null);
public sealed record DemandPosition(long NmId, int Units);
public sealed record DemandPayload(RecommendationWindow Window, IReadOnlyList<DemandPosition> Items);
public sealed record OperationalPage(int Offset, DateTime StartedAtUtc, RecommendationWindow? Window = null,
    IReadOnlyList<StockPosition>? Stocks = null, IReadOnlyList<DemandPosition>? Demand = null, Guid? TaskId = null, int Stage = 0);
public sealed record OperationalRead(object Payload, OperationalPage? Next = null, DateTime? StartedAtUtc = null);

public static class OperationalParsing
{
    public static decimal Money(JsonElement row, string field)
    {
        if (!row.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var n) || n < 0) throw new JsonException("Некорректное поле " + field);
        return n;
    }
    public static void Rubles(JsonElement row)
    {
        if (!row.TryGetProperty("currency", out var c) || c.GetString() != "RUB") throw new JsonException("Валюта источника не подтверждена как RUB.");
    }
    public static StockPosition[] Stocks(JsonElement root)
    {
        var items = root.GetProperty("data").GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array) throw new JsonException("Нет массива остатков.");
        return items.EnumerateArray().Select(x => new StockPosition(x.GetProperty("nmId").GetInt64(), x.GetProperty("chrtId").GetInt64(),
            x.GetProperty("warehouseId").GetInt64(), Money(x, "quantity"))).ToArray();
    }
    public static StockPosition[] FbwReport(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("Отчёт остатков не является массивом.");
        var result = root.EnumerateArray().Select(row =>
        {
            var warehouses = row.GetProperty("warehouses").EnumerateArray().Select(w =>
                new WarehouseRemain(w.GetProperty("warehouseName").GetString() ?? throw new JsonException(), Money(w, "quantity"))).ToArray();
            // Use the explicit stock-only total. Never add transit or re-add the warehouse breakdown.
            var totals = warehouses.Where(x => x.WarehouseName == "Total in warehouses").ToArray();
            if (totals.Length != 1) throw new JsonException("Нет однозначного итога доступных остатков WB.");
            var nm = row.GetProperty("nmId").GetInt64();
            var barcode = row.GetProperty("barcode").GetString(); var size = row.GetProperty("techSize").GetString();
            if (nm <= 0 || string.IsNullOrWhiteSpace(barcode) || size == null || totals[0].Quantity != decimal.Truncate(totals[0].Quantity)) throw new JsonException("Неполная строка остатков WB.");
            return new StockPosition(nm, null, null, totals[0].Quantity, barcode, size, warehouses);
        }).ToArray();
        if (result.DistinctBy(x => (x.NmId, x.Barcode, x.Size)).Count() != result.Length) throw new JsonException("Повторная строка отчёта остатков WB.");
        return result;
    }
}

public sealed class OperationalSync(EcomadsDbContext db, IWbOperationsClient client, IWbPromotionClient promotion,
    IWbTokenService tokens, ILogger<OperationalSync> logger, WbSyncPlanner planner,
    IOptions<RecommendationOptions>? options = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task RefreshAsync(Store store, DateTime now, CancellationToken ct)
    {
        var token = tokens.Unprotect(store.ApiKey!);
        var claims = tokens.ReadClaims(token);
        async Task Failure(WbOperationalSnapshot row, Exception error, string method)
        {
            row.State = error is WbApiException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized } ? "access_denied" : "source_error";
            row.ErrorCode = error is WbApiException wb ? "http_" + (int)wb.StatusCode : error is NotSupportedException ? "token_category_unavailable" : "invalid_source";
            if (error is WbApiException { RetryAfter: { } retry }) await WbRateLimits.DeferUntilAsync(db, store.Id, method, 0, now.Add(retry), ct);
            logger.LogWarning("Operational source {Source} unavailable for {StoreId}: {Code}", row.Source, store.Id, row.ErrorCode);
            if (row.ErrorCode == "invalid_source")
                logger.LogWarning(error, "Operational response validation failed for {Source}", row.Source);
        }
        async Task Capture(string source, Func<OperationalPage?, Task<OperationalRead>> read, TimeSpan interval,
            Func<OperationalPage?, string>? methodFor = null)
        {
            var last = await db.WbOperationalSnapshots.AsNoTracking().Where(x => x.StoreId == store.Id && x.Source == source && x.EntityKey == "")
                .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(ct);
            var newDemandDay = source == "demand" && (last == null || WbSyncPlanner.Today(last.RequestedAtUtc) != WbSyncPlanner.Today(now));
            var retryInterval = last?.State is "source_error" or "access_denied" ? TimeSpan.FromMinutes(30) : interval;
            if (last?.State != "collecting" && !newDemandDay && last?.RequestedAtUtc > now.Subtract(retryInterval)) return;
            OperationalPage? checkpoint = null;
            if (last?.State == "collecting")
            {
                try { checkpoint = JsonSerializer.Deserialize<OperationalPage>(last.PayloadJson, Json); }
                catch (JsonException) { }
            }
            var method = methodFor?.Invoke(checkpoint) ?? "operations_" + source;
            if (!await WbRateLimits.TryReserveAsync(db, store.Id, method, 0, now, ct)) return;
            var row = new WbOperationalSnapshot { Id = Guid.NewGuid(), StoreId = store.Id, Source = source, RequestedAtUtc = now, ReceivedAtUtc = now };
            try
            {
                if (source == "demand" && checkpoint?.Window != RecommendationPeriods.Economic(now)) checkpoint = null;
                var readResult = await read(checkpoint);
                row.State = readResult.Next == null ? "ready" : "collecting";
                row.PayloadJson = JsonSerializer.Serialize(readResult.Next ?? readResult.Payload, Json);
                row.CoverageStartedAtUtc = readResult.StartedAtUtc ?? checkpoint?.StartedAtUtc ?? now;
            }
            catch (Exception error) when (error is not OperationCanceledException) { await Failure(row, error, method); }
            row.ReceivedAtUtc = DateTime.UtcNow;
            db.WbOperationalSnapshots.Add(row); await db.SaveChangesAsync(ct);
        }
        // Each pass fetches one page per source; checkpoints survive process restarts and rate limits.
        if (options?.Value.Rule01Enabled == true)
        {
        foreach (var fbs in new[] { false, true })
        {
        if (!fbs && claims.TokenType == 1)
        {
            await Capture("stocks_fbw", async previous =>
            {
                if (!claims.HasAnalytics) throw new NotSupportedException();
                if (previous == null)
                {
                    using var created = await client.CreateFbwReportAsync(token, ct);
                    var task = created.RootElement.GetProperty("data").GetProperty("taskId").GetGuid();
                    if (task == Guid.Empty) throw new JsonException("Не получен ID отчёта.");
                    return new(new { }, new(0, now, TaskId: task, Stage: 1));
                }
                if (previous.StartedAtUtc < now.AddHours(-1) || previous.TaskId == null) throw new JsonException("Отчёт остатков не получен за час.");
                if (previous.Stage == 1)
                {
                    using var status = await client.FbwReportStatusAsync(token, previous.TaskId.Value, ct);
                    var state = status.RootElement.GetProperty("data").GetProperty("status").GetString();
                    if (state is not ("new" or "processing" or "done")) throw new JsonException("Не удалось сформировать отчёт остатков.");
                    return new(new { }, previous with { Stage = state == "done" ? 2 : 1 });
                }
                using var report = await client.DownloadFbwReportAsync(token, previous.TaskId.Value, ct);
                return new(OperationalParsing.FbwReport(report.RootElement), StartedAtUtc: previous.StartedAtUtc);
            }, TimeSpan.FromMinutes(30), p => "operations_fbw_report_" + (p?.Stage ?? 0));
            continue;
        }
        await Capture(fbs ? "stocks_fbs" : "stocks_fbw", async previous =>
        {
            if (!claims.HasAnalytics) throw new NotSupportedException();
            var offset = previous?.Offset ?? 0;
            if (offset > 2_500_000) throw new JsonException("Пагинация остатков не завершена.");
            using var page = await client.StocksAsync(token, fbs, offset, ct);
            var positions = OperationalParsing.Stocks(page.RootElement);
            var rows = (previous?.Stocks ?? []).Concat(positions).ToArray();
            if (rows.Any(x => x.NmId <= 0 || x.ChrtId <= 0 || x.Quantity != decimal.Truncate(x.Quantity)) ||
                rows.DistinctBy(x => (x.NmId, x.ChrtId, x.WarehouseId)).Count() != rows.Length) throw new JsonException("Повторная или некорректная строка остатков.");
            return new(rows, positions.Length < 250000 ? null : new(offset + 250000, previous?.StartedAtUtc ?? now, Stocks: rows));
        }, TimeSpan.FromMinutes(30));
        }

        await Capture("demand", async previous =>
        {
            if (!claims.HasAnalytics) throw new NotSupportedException();
            var window = RecommendationPeriods.Economic(now); var offset = previous?.Offset ?? 0;
            if (offset > 100000) throw new JsonException("Пагинация воронки не завершена.");
            using var page = await client.DemandAsync(token, window.StartDate, window.EndDate, offset, ct);
            var data = page.RootElement.GetProperty("data"); OperationalParsing.Rubles(data);
            var products = data.GetProperty("products"); var items = (previous?.Demand ?? []).ToList();
            foreach (var product in products.EnumerateArray())
            {
                var nm = product.GetProperty("product").GetProperty("nmId").GetInt64();
                var q = product.GetProperty("statistic").GetProperty("selected").GetProperty("orderCount").GetInt32();
                if (nm <= 0 || q < 0 || items.Any(x => x.NmId == nm)) throw new JsonException("Повторный товар или некорректное количество.");
                items.Add(new(nm, q));
            }
            return new(new DemandPayload(window, items), products.GetArrayLength() < 1000 ? null : new(offset + 1000, previous?.StartedAtUtc ?? now, window, Demand: items));
        }, TimeSpan.FromHours(6));
        }

        await Capture("balance", async _ =>
        {
            using var document = await client.BalanceAsync(token, ct); OperationalParsing.Rubles(document.RootElement);
            return new(new { amount = OperationalParsing.Money(document.RootElement, "balance") + OperationalParsing.Money(document.RootElement, "net") });
        }, TimeSpan.FromMinutes(30));

        await Capture("advertising", async _ =>
        {
            var adverts = await promotion.GetCampaignsAsync(token, ct);
            // This uses the shared campaign-list rate slot, so it must also feed the regular import.
            await planner.UpsertCampaignsAsync(store, adverts, ct);
            return new(adverts);
        }, TimeSpan.FromMinutes(30));

        // Rotate oldest campaign budgets first; each campaign has independent freshness/evidence.
        var budgets = await db.WbOperationalSnapshots.AsNoTracking().Where(x => x.StoreId == store.Id && x.Source == "budgets")
            .GroupBy(x => x.EntityKey).Select(g => new { Id = g.Key, At = g.Max(x => x.RequestedAtUtc) }).ToListAsync(ct);
        var ids = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == store.Id && (x.WbStatus == 4 || x.WbStatus == 9 || x.WbStatus == 11)).Select(x => x.WbCampaignId).ToListAsync(ct);
        var selected = ids.Distinct().Where(id => !budgets.Any(b => b.Id == id && b.At > now.AddMinutes(-15)))
            .OrderBy(id => budgets.FirstOrDefault(b => b.Id == id)?.At ?? DateTime.MinValue).ThenBy(id => id, StringComparer.Ordinal).Take(50).ToArray();
        if (selected.Length > 0 && await WbRateLimits.TryReserveAsync(db, store.Id, "operations_budgets", 0, now, ct))
        {
            var rows = selected.Select(id => new WbOperationalSnapshot { Id = Guid.NewGuid(), StoreId = store.Id, Source = "budgets", EntityKey = id,
                RequestedAtUtc = now, ReceivedAtUtc = now, State = "source_error", ErrorCode = "pair_missing" }).ToArray();
            try
            {
                using var document = await client.BudgetsAsync(token, selected.Select(long.Parse).ToArray(), ct);
                var seen = new HashSet<string>();
                foreach (var advert in document.RootElement.GetProperty("adverts").EnumerateArray())
                {
                    var id = advert.GetProperty("advertId").GetInt64().ToString();
                    if (!seen.Add(id) || !selected.Contains(id)) throw new JsonException("Повторный или незапрошенный бюджет.");
                    var row = rows.Single(x => x.EntityKey == id);
                    try
                    {
                        OperationalParsing.Rubles(advert);
                        row.PayloadJson = JsonSerializer.Serialize(new Dictionary<string, decimal> { [id] = OperationalParsing.Money(advert, "total") }, Json);
                        row.State = "ready"; row.ErrorCode = null;
                    }
                    catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                    { row.PayloadJson = "{}"; await Failure(row, error, "operations_budgets"); }
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { foreach (var row in rows) { row.PayloadJson = "{}"; await Failure(row, error, "operations_budgets"); } }
            foreach (var row in rows) { row.ReceivedAtUtc = DateTime.UtcNow; db.WbOperationalSnapshots.Add(row); }
            await db.SaveChangesAsync(ct);
        }
    }
}

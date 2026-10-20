using System.Security.Cryptography;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbProductOrdersImporter(EcomadsDbContext db)
{
    public const int Version = 1;
    public static readonly string[] Kinds = ["funnel_month", "funnel_backfill", "funnel_year", "product_orders"];
    public sealed record Product(long NmId, decimal OrderSum, int OrderCount, decimal PastOrderSum, int PastOrderCount);

    public static Product[] ReadPage(JsonElement root)
    {
        // Reuse the strict validation of mandatory periods, metrics and unique IDs.
        WbSalesFunnelImporter.ReadProductsPage(root, new());
        var data = root.GetProperty("data");
        CheckCurrency(root); CheckCurrency(data);
        return data.GetProperty("products").EnumerateArray().Select(p =>
        {
            CheckCurrency(p);
            var descriptor = p.TryGetProperty("product", out var nested) ? nested : p;
            CheckCurrency(descriptor);
            var stat = p.GetProperty("statistic");
            var selected = stat.GetProperty("selected"); var past = stat.GetProperty("past");
            foreach (var amount in new[] { selected.GetProperty("orderSum").GetDecimal(), past.GetProperty("orderSum").GetDecimal() })
                if (amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
                    throw new JsonException("Сумма не соответствует точности товарного хранилища.");
            return new Product(descriptor.GetProperty("nmId").GetInt64(), selected.GetProperty("orderSum").GetDecimal(),
                selected.GetProperty("orderCount").GetInt32(), past.GetProperty("orderSum").GetDecimal(), past.GetProperty("orderCount").GetInt32());
        }).ToArray();
    }

    private static void CheckCurrency(JsonElement row)
    {
        if (row.TryGetProperty("currency", out var currency) &&
            (currency.ValueKind != JsonValueKind.String || currency.GetString() != "RUB"))
            throw new JsonException("Товарная воронка должна содержать суммы в RUB.");
    }

    // Caller holds the store ingestion lock and transaction. Replays never advance jobs
    // or rewrite existing store totals. A broken chain cannot publish even its first page.
    public async Task ApplyAsync(WbResponseSnapshot terminal, CancellationToken ct)
    {
        if (terminal.ProductOrdersVersion >= Version) return;
        if (!Kinds.Contains(terminal.Kind)) return;
        terminal.ProductOrdersError = null;
        try
        {
            await ValidateAndPublishAsync(terminal, ct);
        }
        catch (Exception e) when (e is JsonException or FormatException or OverflowException or KeyNotFoundException)
        {
            terminal.ProductOrdersError = "invalid_product_snapshot";
            await MarkUnavailableAsync(terminal, "ambiguous", ct);
        }
        terminal.ProductOrdersVersion = Version;
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateAndPublishAsync(WbResponseSnapshot terminal, CancellationToken ct)
    {
        var job = JsonSerializer.Deserialize<WbSyncJob>(terminal.JobStateJson) ?? throw new JsonException("Нет состояния задания.");
        var ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [];
        if (job.StoreId != terminal.StoreId || job.Kind != terminal.Kind || job.NextCampaignOffset < 0 || job.NextCampaignOffset >= ids.Length)
            throw new JsonException("Не совпадает область снимка.");
        var past = DateOnly.ParseExact(ids[job.NextCampaignOffset].ToString(), "yyyyMMdd");
        var selected = past.AddDays(1);
        var previous = JsonSerializer.Deserialize<WbFunnelPageState>(job.PairIdsJson ?? "null") ?? new();
        var chainIds = (previous.SnapshotIds ?? []).Append(terminal.Id).ToArray();
        if (chainIds.Distinct().Count() != chainIds.Length || previous.Offset != (chainIds.Length - 1) * 1000)
            throw new JsonException("Неполная цепочка страниц.");
        var snapshots = await db.WbResponseSnapshots.AsNoTracking().Where(x => chainIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (snapshots.Count != chainIds.Length) throw new JsonException("Отсутствует страница снимка.");
        var products = new List<Product>(); var seen = new HashSet<long>();
        DateTime? started = null;
        for (var index = 0; index < chainIds.Length; index++)
        {
            var page = snapshots[chainIds[index]];
            var pageJob = JsonSerializer.Deserialize<WbSyncJob>(page.JobStateJson) ?? throw new JsonException();
            var pageState = JsonSerializer.Deserialize<WbFunnelPageState>(pageJob.PairIdsJson ?? "null") ?? new();
            var pageIds = JsonSerializer.Deserialize<long[]>(pageJob.CampaignIdsJson) ?? [];
            if (page.StoreId != terminal.StoreId || page.Kind != terminal.Kind || page.JobId != pageJob.Id ||
                page.HttpStatus is not (>= 200 and < 300) || pageJob.StoreId != terminal.StoreId ||
                pageJob.NextCampaignOffset != job.NextCampaignOffset || pageState.Offset != index * 1000 ||
                !(pageState.SnapshotIds ?? []).SequenceEqual(chainIds.Take(index)) ||
                pageIds.Length <= job.NextCampaignOffset || pageIds[job.NextCampaignOffset] != ids[job.NextCampaignOffset] ||
                (index < chainIds.Length - 1 && page.State != "applied")) throw new JsonException("Не совпадают страницы снимка.");
            if (page.RequestBody != null)
            {
                using var request = JsonDocument.Parse(page.RequestBody);
                var req = request.RootElement;
                if (req.GetProperty("selectedPeriod").GetProperty("start").GetString() != selected.ToString("yyyy-MM-dd") ||
                    req.GetProperty("selectedPeriod").GetProperty("end").GetString() != selected.ToString("yyyy-MM-dd") ||
                    req.GetProperty("pastPeriod").GetProperty("start").GetString() != past.ToString("yyyy-MM-dd") ||
                    req.GetProperty("pastPeriod").GetProperty("end").GetString() != past.ToString("yyyy-MM-dd") ||
                    req.GetProperty("offset").GetInt32() != index * 1000 ||
                    new[] { "nmIds", "brandNames", "subjectIds", "tagIds" }.Any(key => req.GetProperty(key).GetArrayLength() != 0))
                    throw new JsonException("Снимок не является полной товарной воронкой выбранных дат.");
            }
            var bytes = page.GetBodyBytes();
            if (page.Sha256 != Convert.ToHexString(SHA256.HashData(bytes))) throw new JsonException("Неверная контрольная сумма.");
            var bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            using var json = JsonDocument.Parse(bytes.AsMemory(bom));
            var rows = ReadPage(json.RootElement);
            if (index == chainIds.Length - 1 && rows.Length == 1000)
                return; // Still a page, not a complete catalogue snapshot.
            if (index < chainIds.Length - 1 && rows.Length != 1000) throw new JsonException("Лишняя страница после завершения.");
            if (rows.Any(row => !seen.Add(row.NmId))) throw new JsonException("Повторный товар между страницами.");
            products.AddRange(rows); started ??= page.RequestedAtUtc;
        }
        // Everything is validated before touching published rows. Empty responses are
        // not proof that all catalogue products have zero orders.
        if (products.Count == 0) { await MarkUnavailableAsync(terminal, "no_data", ct); return; }
        var nowDay = WbSyncPlanner.Today();
        foreach (var date in new[] { past, selected }.Where(x => x >= nowDay.AddDays(-30) && x < nowDay))
        {
            var old = await db.WbProductOrderDays.FindAsync([terminal.StoreId, date], ct);
            if (old != null && (old.LoadedAtUtc > started || old.SnapshotId == terminal.Id)) continue;
            var quality = await db.WbSourceQualities.FindAsync([terminal.StoreId, "product_orders", "", date, date], ct);
            if (quality != null && quality.LastAttemptAtUtc > started) continue;
            if (quality == null)
            {
                quality = new() { StoreId = terminal.StoreId, Source = "product_orders", Entity = "", StartDate = date, EndDate = date };
                db.WbSourceQualities.Add(quality);
            }
            quality.State = "data"; quality.LastAttemptAtUtc = started!.Value; quality.AcceptedAtUtc = started;
            quality.SnapshotId = terminal.Id; quality.AcceptedSnapshotId = terminal.Id;
            if (old == null) { old = new() { StoreId = terminal.StoreId, Date = date }; db.WbProductOrderDays.Add(old); }
            else await db.WbProductDailyOrders.Where(x => x.StoreId == terminal.StoreId && x.Date == date).ExecuteDeleteAsync(ct);
            old.SnapshotId = terminal.Id; old.LoadedAtUtc = started.Value; old.ProductCount = products.Count;
            db.WbProductDailyOrders.AddRange(products.Select(p => new WbProductDailyOrders {
                StoreId = terminal.StoreId, Date = date, NmId = p.NmId,
                OrderSum = date == selected ? p.OrderSum : p.PastOrderSum,
                OrderCount = date == selected ? p.OrderCount : p.PastOrderCount }));
        }
    }

    private async Task MarkUnavailableAsync(WbResponseSnapshot snapshot, string state, CancellationToken ct)
    {
        if (WbIngestionContext.Current is { } context) context.HasIssues = true;
        snapshot.ProductOrdersError ??= state == "no_data" ? "no_data" : "invalid_product_snapshot";
        WbSyncJob? job;
        try { job = JsonSerializer.Deserialize<WbSyncJob>(snapshot.JobStateJson); }
        catch (JsonException) { return; }
        if (job == null || job.StoreId != snapshot.StoreId) return;
        long[] ids;
        try { ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []; }
        catch (JsonException) { return; }
        if (job.NextCampaignOffset < 0 || job.NextCampaignOffset >= ids.Length ||
            !DateOnly.TryParseExact(ids[job.NextCampaignOffset].ToString(), "yyyyMMdd", out var past)) return;
        foreach (var day in new[] { past, past.AddDays(1) })
        {
            var q = await db.WbSourceQualities.FindAsync([snapshot.StoreId, "product_orders", "", day, day], ct);
            if (q != null && q.LastAttemptAtUtc > snapshot.RequestedAtUtc) continue;
            if (q == null) { q = new() { StoreId = snapshot.StoreId, Source = "product_orders", Entity = "", StartDate = day, EndDate = day }; db.WbSourceQualities.Add(q); }
            q.State = state; q.LastAttemptAtUtc = snapshot.RequestedAtUtc; q.SnapshotId = snapshot.Id;
            q.Reason = "Последний товарный снимок не подтверждает данные дня.";
        }
    }
}

using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public static class WbRateLimits
{
    public static string MethodFor(string kind, int offset = 0) => kind switch
    {
        "operations_advertising" => "campaign_list",
        "operations_demand" => "funnel_products",
        "product_orders" => "funnel_products",
        "fullstats_month" => "fullstats",
        "expenses_month" => "expenses",
        "funnel_month" => "funnel_products",
        "archive" => "fullstats",
        "fullstats_history" => "fullstats",
        "expenses_history" => "expenses",
        "funnel_recent" => "funnel_history",
        "funnel_backfill" => "funnel_products",
        "funnel_year" => "funnel_products",
        "expenses" => "expenses",
        "campaign_list" => "campaign_list",
        _ => kind
    };

    public static TimeSpan IntervalFor(string kind) => kind switch
    {
        // One request to each read endpoint per pass: basic tokens allow one per 12 minutes.
        "cluster_settings" => TimeSpan.FromMinutes(12),
        "operations_fbw_report_0" or "operations_fbw_report_1" or "operations_fbw_report_2" => TimeSpan.FromMinutes(15),
        "operations_stocks_fbw" or "operations_stocks_fbs" => TimeSpan.FromSeconds(21),
        "operations_budgets" => TimeSpan.FromMinutes(15),
        "operations_balance" or "operations_demand" => TimeSpan.FromMinutes(30),
        "clusters" or "funnel_recent" or "funnel_backfill" or "funnel_year" or "product_orders" or
            "funnel_history" or "funnel_products" or "funnel_month" => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(1)
    };

    public static async Task<DateTime> NextSlotAsync(EcomadsDbContext db, Guid storeId, string kind,
        DateTime now, CancellationToken cancellationToken)
    {
        var method = MethodFor(kind);
        var last = await db.WbMethodSlots.Where(x => x.StoreId == storeId && x.Method == method)
            .Select(x => (DateTime?)x.LastRequestAtUtc).FirstOrDefaultAsync(cancellationToken);
        var next = last?.Add(IntervalFor(kind));
        return next > now ? next.Value : now;
    }

    public static async Task<bool> TryReserveAsync(EcomadsDbContext db, Guid storeId, string kind,
        int offset, DateTime now, CancellationToken cancellationToken)
    {
        var method = MethodFor(kind, offset);
        var threshold = now.Subtract(IntervalFor(kind));
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO wb_method_slots (store_id, method, last_request_at_utc)
            VALUES ({storeId}, {method}, {now})
            ON CONFLICT (store_id, method) DO UPDATE SET last_request_at_utc = EXCLUDED.last_request_at_utc
            WHERE wb_method_slots.last_request_at_utc <= {threshold}", cancellationToken);
        return changed == 1;
    }

    public static Task DeferUntilAsync(EcomadsDbContext db, Guid storeId, string kind,
        int offset, DateTime retryAt, CancellationToken cancellationToken)
    {
        var method = MethodFor(kind, offset);
        var lastRequest = retryAt.Subtract(IntervalFor(kind));
        return db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO wb_method_slots (store_id, method, last_request_at_utc)
            VALUES ({storeId}, {method}, {lastRequest})
            ON CONFLICT (store_id, method) DO UPDATE
            SET last_request_at_utc = GREATEST(wb_method_slots.last_request_at_utc,
                EXCLUDED.last_request_at_utc)", cancellationToken);
    }
}

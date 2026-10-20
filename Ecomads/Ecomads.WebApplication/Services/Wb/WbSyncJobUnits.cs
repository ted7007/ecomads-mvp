using System.Text.Json;
using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

public static class WbSyncJobUnits
{
    public static readonly string[] SupportedKinds = ["fullstats_month", "expenses_month", "funnel_month", "fullstats", "funnel_recent", "expenses", "fullstats_history", "expenses_history", "funnel_year", "clusters", "jam", "archive", "funnel_backfill", "product_orders"];
    public static bool CanPause(string kind) => IsYearHistory(kind) || kind == "product_orders";

    public static bool IsYearHistory(string kind) => kind is "fullstats_history" or "expenses_history" or "funnel_year";
    public static int Total(WbSyncJob job) => job.Kind switch
    {
        "fullstats_history" or "expenses_history" or "expenses_month" =>
            (JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson ?? "[]") ?? []).Length,
        "clusters" => (JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson ?? "[]") ?? []).Length,
        "funnel_recent" => 1,
        "expenses" => job.PairIdsJson == null ? 1 : (JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson) ?? []).Length,
        "funnel_backfill" or "funnel_month" => (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Length,
        _ => (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Length
    };

    public static int BatchSize(string kind) => kind switch
    {
        "clusters" => 100,
        "fullstats_history" or "expenses_history" or "expenses_month" => 1,
        "funnel_recent" or "funnel_backfill" or "funnel_year" or "funnel_month" or "product_orders" or "expenses" => 1,
        _ => 50
    };

    public static string Unit(string kind) => kind switch
    {
        "clusters" => "pair",
        "fullstats_history" or "expenses_history" or "expenses_month" => "request",
        "jam" => "product",
        "funnel_recent" or "funnel_backfill" or "funnel_year" or "funnel_month" or "product_orders" or "expenses" => "request",
        _ => "campaign"
    };
}

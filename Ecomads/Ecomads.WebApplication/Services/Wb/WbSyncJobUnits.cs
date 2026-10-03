using System.Text.Json;
using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

public static class WbSyncJobUnits
{
    public static int Total(WbSyncJob job) => job.Kind switch
    {
        "clusters" => (JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson ?? "[]") ?? []).Length,
        "funnel" => 1 + (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Length,
        "funnel_recent" => 1,
        "expenses" => 1,
        "funnel_backfill" => (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Length,
        _ => (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Length
    };

    public static int BatchSize(string kind) => kind switch
    {
        "clusters" => 100,
        "funnel" or "funnel_recent" or "funnel_backfill" or "expenses" => 1,
        _ => 50
    };

    public static string Unit(string kind) => kind switch
    {
        "clusters" => "pair",
        "jam" => "product",
        "funnel" or "funnel_recent" or "funnel_backfill" or "expenses" => "request",
        _ => "campaign"
    };
}

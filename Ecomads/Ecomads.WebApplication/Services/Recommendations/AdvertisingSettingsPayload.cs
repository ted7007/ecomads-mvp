using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record AdvertisingCluster(string NomenclatureId, string Cluster, decimal? BidRubles);
public sealed record AdvertisingMinus(string NomenclatureId, IReadOnlyList<string> Clusters);
public sealed record AdvertisingSettingsPayload(IReadOnlyList<AdvertisingCluster> Bids, IReadOnlyList<AdvertisingMinus> Minus,
    WbCampaignInfo? Campaign = null, DateTime? CampaignReceivedAtUtc = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static AdvertisingControl Control(WbAdvertisingSettings? source, string nm, string cluster, DateTime now)
    {
        if (source is null) return new("unknown");
        // CPC metadata is available, but WB's cluster bid/minus endpoints only support CPM.
        // Do not present that API limitation as a failed settings import.
        try
        {
            var metadata = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(source.PayloadJson, Json);
            if (metadata?.Campaign?.PaymentType == "cpc" && metadata.CampaignReceivedAtUtc >= now.AddHours(-24))
                return new("unsupported", metadata.Campaign.BidType, "cpc", ReceivedAtUtc: metadata.CampaignReceivedAtUtc);
        }
        catch (JsonException) { }
        if (source.Status != "ready") return new(source.Status);
        if (!source.ReceivedAtUtc.HasValue || source.ReceivedAtUtc < now.AddHours(-24)) return new("stale");
        try
        {
            var payload = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(source.PayloadJson, Json)!;
            if (payload?.Bids is null || payload.Minus is null || payload.Minus.Any(x => x.Clusters is null)) return new("invalid_source");
            var minus = payload.Minus.SingleOrDefault(x => x.NomenclatureId == nm);
            if (minus == null) return new("unknown", source.BidType, source.PaymentType, ReceivedAtUtc: source.ReceivedAtUtc);
            return new("ready", source.BidType, source.PaymentType, minus?.Clusters.Any(x => string.Equals(x.Trim(), cluster.Trim(), StringComparison.OrdinalIgnoreCase)),
                payload.Bids.FirstOrDefault(x => x.NomenclatureId == nm && string.Equals(x.Cluster.Trim(), cluster.Trim(), StringComparison.OrdinalIgnoreCase))?.BidRubles
                    ?? payload.Campaign?.ArticleBids?.FirstOrDefault(x => x.NmId.ToString() == nm)?.SearchRubles,
                ReceivedAtUtc: source.ReceivedAtUtc);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return new("invalid_source"); }
    }
}

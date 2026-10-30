using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record ResultSetting(string Id, DateTime AtUtc, string Source, string State,
    bool? Excluded = null, decimal[]? Bids = null, int? Status = null, bool? Member = null, decimal? Budget = null);

public sealed class ResultObservationReader(EcomadsDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Source(RecommendationCard card) => card.Presentation?.Primary.Kind switch {
        "exclude" => "settings", "reduce_bid" => "settings", "top_up_budget" => "budgets", _ => "advertising" };
    public async Task<ResultSetting[]> ReadAsync(Campaign campaign, RecommendationCard card, DateTime since, CancellationToken ct, DateTime? until = null)
    {
        var source = Source(card); var result = new List<ResultSetting>();
        if (source == "settings")
        {
            var rows = await db.WbResultObservations.AsNoTracking().Where(x => x.CampaignId == campaign.Id && x.AttemptedAtUtc >= since && (until == null || x.AttemptedAtUtc <= until))
                .OrderBy(x => x.AttemptedAtUtc).ToArrayAsync(ct);
            foreach (var row in rows)
            {
                ResultSetting parsed = new(row.Id.ToString(), row.State == "ready" ? row.ReceivedAtUtc ?? row.AttemptedAtUtc : row.AttemptedAtUtc, source, row.State);
                try {
                    var p = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(row.PayloadJson, Json);
                    var nm = card.Subject.NomenclatureId;
                    var minus = p?.Minus?.SingleOrDefault(x => x.NomenclatureId == nm);
                    var bid = p?.Bids?.SingleOrDefault(x => x.NomenclatureId == nm &&
                        RecommendationLifecycle.NormalizeCluster(x.Cluster) == RecommendationLifecycle.NormalizeCluster(card.Subject.ClusterName))?.BidRubles;
                    parsed = parsed with { Excluded = minus?.Clusters.Any(x => RecommendationLifecycle.NormalizeCluster(x) ==
                        RecommendationLifecycle.NormalizeCluster(card.Subject.ClusterName)), Bids = bid.HasValue ? [bid.Value] : null };
                } catch (Exception e) when (e is JsonException or InvalidOperationException) { parsed = parsed with { State = "invalid_source" }; }
                result.Add(parsed);
            }
        }
        else
        {
            var rows = await db.WbOperationalSnapshots.AsNoTracking().Where(x => x.StoreId == campaign.StoreId && x.Source == source &&
                (source != "budgets" || x.EntityKey == campaign.WbCampaignId) && x.RequestedAtUtc >= since && (until == null || x.RequestedAtUtc <= until)).OrderBy(x => x.RequestedAtUtc).ToArrayAsync(ct);
            foreach (var row in rows)
            {
                ResultSetting parsed = new(row.Id.ToString(), row.CoverageStartedAtUtc ?? row.RequestedAtUtc, source, row.State);
                try {
                    if (source == "budgets") {
                        var budgets = JsonSerializer.Deserialize<Dictionary<string, decimal>>(row.PayloadJson, Json);
                        parsed = budgets?.TryGetValue(campaign.WbCampaignId, out var b) == true
                            ? parsed with { Budget = b } : parsed with { State = "unknown" };
                    }
                    else {
                        var p = JsonSerializer.Deserialize<WbCampaignInfo[]>(row.PayloadJson, Json)?.SingleOrDefault(x => x.Id.ToString() == campaign.WbCampaignId);
                        var bid = p?.ArticleBids?.SingleOrDefault(x => x.NmId.ToString() == card.Subject.NomenclatureId);
                        // Keep placement identity: never compare a search bid with a recommendation bid.
                        parsed = parsed with { Status = p?.Status, Member = p == null ? null : p.NomenclatureIds.Any(x => x.ToString() == card.Subject.NomenclatureId),
                            Bids = bid == null ? null : [bid.SearchRubles ?? -1, bid.RecommendationRubles ?? -1] };
                    }
                } catch (Exception e) when (e is JsonException or InvalidOperationException) { parsed = parsed with { State = "invalid_source" }; }
                result.Add(parsed);
            }
        }
        return result.Where(x => x.AtUtc >= since && (until == null || x.AtUtc <= until)).OrderBy(x => x.AtUtc).ToArray();
    }

    public static bool Applied(RecommendationCard card, ResultSetting before, ResultSetting after)
    {
        if (before.State != "ready" || after.State != "ready" || after.AtUtc <= before.AtUtc) return false;
        var action = card.Presentation!.Primary;
        return action.Kind switch {
            "exclude" => before.Excluded == false && after.Excluded == true,
            "pause_campaign" => before.Status == 9 && after.Status == 11,
            "remove_article" => before.Member == true && after.Member == false,
            "top_up_budget" => before.Budget.HasValue && after.Budget - before.Budget >= card.Presentation.Details?.TopUp / 2m && card.Presentation.Details?.TopUp > 0,
            "reduce_bid" or "reduce_article_bid" or "reduce_campaign_bid" => Reduced(action, before.Bids, after.Bids),
            _ => false
        };
    }
    public static bool Known(RecommendationCard card, ResultSetting state) => state.State == "ready" && (card.Presentation!.Primary.Kind switch {
        "exclude" => state.Excluded.HasValue,
        "top_up_budget" => state.Budget is >= 0,
        "pause_campaign" => state.Status.HasValue,
        "remove_article" => state.Member.HasValue,
        _ => state.Bids?.Any(x => x > 0) == true
    });
    public static bool AdvanceBaseline(RecommendationCard card, ResultSetting before, ResultSetting current) => card.Presentation!.Primary.Kind switch {
        "exclude" => before.Excluded == current.Excluded,
        "top_up_budget" => current.Budget <= before.Budget,
        "pause_campaign" => current.Status == before.Status || current.Status == 9,
        "remove_article" => before.Member == current.Member,
        _ => before.Bids != null && current.Bids != null && before.Bids.SequenceEqual(current.Bids)
    };
    private static bool Reduced(RecommendationAction action, decimal[]? before, decimal[]? after)
    {
        if (before == null || after == null || before.Length != after.Length || before.Length == 0) return false;
        var pairs = before.Zip(after).ToArray();
        var known = pairs.Where(p => p.First > 0).ToArray();
        if (known.Length == 0 || pairs.Any(p => (p.First < 0) != (p.Second < 0))) return false;
        return known.All(p => p.Second > 0 && (action.TargetBid.HasValue && before.Length == 1
            ? p.First > action.TargetBid && p.First - p.Second >= (p.First - action.TargetBid) / 2m
            : action.ReductionPercent > 0 && (p.First - p.Second) / p.First >= action.ReductionPercent / 200m));
    }
    public static bool Preserved(ResultMember member, ResultSetting current) => current.State == "ready" &&
        member.Card.Presentation!.Primary.Kind switch {
            "exclude" => current.Excluded == true,
            "pause_campaign" => current.Status == 11,
            "remove_article" => current.Member == false,
            "top_up_budget" => true,
            _ => Applied(member.Card, member.Before, current)
        };
}

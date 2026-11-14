using System.Globalization;
using Ecomads.WebApplication.Services.Wb;

namespace Ecomads.WebApplication.Services.Recommendations;

internal static class BidReduction
{
    internal const string AtMinimum = "bid_at_minimum";
    private const string ClusterMinimumUnknown = "cluster_minimum_unknown";

    internal static RecommendationAction Calculate(decimal percent, decimal? bid, decimal? minimum,
        decimal? step, string kind, string level, string? placement = null)
    {
        if (bid > 0 && bid == minimum)
            return new(AtMinimum, "Наблюдать", Level: level);
        if (percent <= 0)
            return new("watch", "Снижение ставки не требуется", Level: level);
        if (bid is null or <= 0 || minimum is null or <= 0 || minimum > bid)
            return level == "cluster"
                ? new(ClusterMinimumUnknown, "Наблюдать", Level: level)
                : new("watch", "Не удалось проверить минимальную ставку", Level: level);
        if (step is null or <= 0)
            return new("watch", "Не удалось проверить шаг ставки", Level: level);

        var raw = Math.Max(minimum.Value, bid.Value * (1 - Math.Clamp(percent, 0, 30) / 100));
        var target = decimal.Ceiling(raw / step.Value) * step.Value;
        if (target >= bid)
            return new("watch", "Доступный шаг не позволяет снизить ставку", Level: level);

        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var zone = placement switch { "search" => " в поиске", "recommendation" => " в рекомендациях", _ => "" };
        return new(kind, $"Снизьте ставку{zone}: {bid.Value.ToString("0.##", culture)} → {target.ToString("0.##", culture)} ₽",
            decimal.Round((1 - target / bid.Value) * 100, 2), target, level, placement);
    }

    internal static bool ShouldGenerateCard(RecommendationPresentation? presentation) =>
        presentation?.Primary.Kind is not (AtMinimum or ClusterMinimumUnknown);

    internal static (RecommendationAction Primary, IReadOnlyList<RecommendationAction> Alternatives) ForProduct(
        decimal percent, ProductBidSnapshot? snapshot, WbCampaignInfo? metadata, string article, DateTime now)
    {
        var unavailable = new RecommendationAction("watch", "Не удалось проверить минимальную ставку", Level: "article");
        if (snapshot?.Current is not { } current || metadata?.Id != current.AdvertId ||
            metadata.PaymentType != current.PaymentType || !long.TryParse(article, out var nm) ||
            snapshot.CurrentAttemptedAtUtc < now.AddHours(-1) || snapshot.CurrentAttemptedAtUtc > now ||
            snapshot.MinimumAttemptedAtUtc is not { } checkedAt || checkedAt < now.AddHours(-24) || checkedAt > now ||
            snapshot.Minimums == null || snapshot.MinimumPaymentType != current.PaymentType)
            return (unavailable, []);

        var placements = AdvertisingSettingsSync.ProductBidPlacements(current);
        if (placements.Length == 0 || !(snapshot.MinimumPlacements ?? []).SequenceEqual(placements) ||
            (metadata.BidType == "unified" ? !placements.SequenceEqual(["combined"]) : placements.Contains("combined")))
            return (unavailable, []);

        var currentBids = current.Bids.Where(x => x.NmId == nm).ToArray();
        var actions = placements.Select(placement =>
        {
            var amount = currentBids.SingleOrDefault(x => x.Placement == placement)?.Kopecks;
            // v3 can represent the single combined bid in the search slot.
            if (placement == "combined" && amount == null && currentBids is [{ Placement: "search" } single])
                amount = single.Kopecks;
            var minimum = snapshot.Minimums.SingleOrDefault(x => x.NmId == nm && x.Placement == placement)?.Kopecks;
            return Calculate(percent, amount / 100m, minimum / 100m, .01m, "reduce_article_bid", "article", placement);
        }).ToArray();
        var reductions = actions.Where(x => x.Kind == "reduce_article_bid").ToArray();
        return reductions.Length > 0 ? (reductions[0], reductions[1..]) :
            (actions.FirstOrDefault(x => x.Kind != AtMinimum) ?? actions[0], []);
    }
}

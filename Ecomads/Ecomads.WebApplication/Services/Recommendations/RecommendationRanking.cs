namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record RecommendationRankingInfo(int Version, string Queue, decimal? WeeklyInefficientSpend, string Basis);
public sealed record RecommendationSummaryItem(Guid? RunId, RecommendationCard Card);

/// <summary>Presentation policy only: never changes the rule or its permitted action.</summary>
public static class RecommendationRanking
{
    public const int Version = 1;

    public static decimal WeeklySpend(RecommendationCard card, RecommendationWindow window) =>
        card.Presentation?.WeeklySpend ?? card.Facts.Spend / Math.Max(1, window.Days) * 7;

    public static RecommendationResponse Apply(RecommendationResponse report)
    {
        var evaluations = report.Evaluations.GroupBy(x => x.EvaluationId).ToDictionary(g => g.Key, g => g.First());
        var cards = report.Cards.Select(card =>
        {
            evaluations.TryGetValue(card.EvaluationId, out var evaluation);
            var weekly = WeeklySpend(card, report.Window);
            decimal? amount = null;
            var basis = "unavailable";
            if (weekly >= 0 && card.Rule.RuleId == "R03" && card.Facts.OrderedProducts == 0 &&
                card.Economy.AllowedSpendPerUnit.HasValue)
            { amount = weekly; basis = "spend_without_units"; }
            else if (weekly >= 0 && card.Rule.RuleId is "R06" or "R07" && card.Economy.Drr > 0 &&
                card.Norm.Value >= 0)
            { amount = weekly * Math.Max(0, 1 - card.Norm.Value / card.Economy.Drr.Value); basis = "spend_above_norm"; }

            var action = card.Presentation?.Primary;
            var actionable = !card.Preliminary && evaluation?.Quality is
                { Complete: true, FieldsVerified: true, SourcesComparable: true } &&
                (action?.Kind == "exclude" || action?.Kind is "reduce_bid" or "reduce_article_bid" or "reduce_campaign_bid" &&
                    action.ReductionPercent > 0);
            var queue = card.Rule.RuleId is "R01" or "R02" ? "urgent" : actionable ? "actionable" : "review";
            return card with { Ranking = new(Version, queue, amount, basis) };
        });
        var ordered = Order(cards.Select(c => new RecommendationSummaryItem(report.RunId, c)), report.Window)
            .Select(x => x.Card).ToArray();
        return report with { Cards = ordered, Packages = RecommendationActions.Packages(ordered) };
    }

    private static int Queue(RecommendationCard c) => c.Ranking?.Queue switch
    { "urgent" => 0, "actionable" => 1, _ => 2 };

    public static IOrderedEnumerable<RecommendationSummaryItem> Order(IEnumerable<RecommendationSummaryItem> cards,
        RecommendationWindow window) => cards.OrderBy(x => Queue(x.Card))
        .ThenBy(x => Queue(x.Card) == 0 ? x.Card.Presentation?.Priority ?? 4 : 0)
        .ThenByDescending(x => Queue(x.Card) == 0 ? WeeklySpend(x.Card, window) : x.Card.Ranking?.WeeklyInefficientSpend)
        .ThenByDescending(x => WeeklySpend(x.Card, window))
        .ThenBy(x => x.Card.Subject.CampaignId).ThenBy(x => x.Card.Subject.ArticleId)
        .ThenBy(x => x.Card.Subject.NomenclatureId, StringComparer.Ordinal)
        .ThenBy(x => x.Card.Subject.ClusterName, StringComparer.Ordinal)
        .ThenBy(x => x.Card.Rule.RuleId, StringComparer.Ordinal).ThenBy(x => x.Card.EvaluationId);

    public static IReadOnlyList<RecommendationSummaryItem> Top(IEnumerable<RecommendationSummaryItem> cards,
        RecommendationWindow window, int limit = 5)
    {
        var chosen = new List<RecommendationSummaryItem>();
        foreach (var item in Order(cards, window))
        {
            if (chosen.Count >= limit) break;
            if (!chosen.Any(x => Overlaps(x.Card, item.Card))) chosen.Add(item);
        }
        return chosen;
    }

    private static bool CoversSpend(RecommendationCard c) => c.Rule.RuleId is "R03" or "R06" or "R07" ||
        c.Rule.RuleId == "R01" && c.Presentation?.Primary.Kind is
            "pause_campaign" or "remove_article" or "reduce_article_bid" or "reduce_campaign_bid";

    public static bool Overlaps(RecommendationCard a, RecommendationCard b)
    {
        if (a.Subject.CampaignId != b.Subject.CampaignId || !CoversSpend(a) || !CoversSpend(b)) return false;
        if (a.Subject.Scope == "campaign" || b.Subject.Scope == "campaign" ||
            a.Presentation?.Primary.Kind is "pause_campaign" or "reduce_campaign_bid" ||
            b.Presentation?.Primary.Kind is "pause_campaign" or "reduce_campaign_bid") return true;
        var sameArticle = a.Subject.ArticleId.HasValue && b.Subject.ArticleId.HasValue
            ? a.Subject.ArticleId == b.Subject.ArticleId
            : a.Subject.NomenclatureId != null && a.Subject.NomenclatureId == b.Subject.NomenclatureId;
        if (!sameArticle) return false;
        return a.Subject.Scope == "article" || b.Subject.Scope == "article" ||
            string.Equals(a.Subject.ClusterName, b.Subject.ClusterName, StringComparison.Ordinal);
    }
}

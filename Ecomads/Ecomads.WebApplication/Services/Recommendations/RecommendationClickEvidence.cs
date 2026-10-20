namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record RecommendationClickEvidence(decimal Cr, string Source, RecommendationWindow Window,
    decimal? MinimumClicks, string Result)
{
    public static RecommendationClickEvidence Calculate(RecommendationWindow window, long clicks, long units,
        bool complete, int? clusterClicks)
    {
        var actual = complete && clicks >= 100 && units >= 0;
        var cr = actual ? (decimal)units / clicks : .02m;
        decimal? minimum = cr > 0 ? decimal.Ceiling(3m / cr) : null;
        return new(cr, actual ? "actual" : "fallback", window, minimum,
            clusterClicks is null or < 0 ? "unknown" :
            minimum.HasValue && clusterClicks >= minimum ? "sufficient" : "insufficient");
    }
}

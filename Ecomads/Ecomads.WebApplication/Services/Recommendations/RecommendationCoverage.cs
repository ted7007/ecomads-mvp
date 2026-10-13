using Ecomads.WebApplication.Services.Wb;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record ClusterCoverageInterval(DateOnly Start, DateOnly End, IReadOnlyList<WbNormQueryPair> Pairs);

public static class RecommendationCoverage
{
    public static bool Covers(RecommendationWindow window, long advertId, long nmId,
        IReadOnlyList<ClusterCoverageInterval> intervals) => window.IsValid &&
        Enumerable.Range(0, window.Days).All(offset => intervals.Any(interval =>
            interval.Start <= window.StartDate.AddDays(offset) && interval.End >= window.StartDate.AddDays(offset) &&
            interval.Pairs.Any(pair => pair.AdvertId == advertId && pair.NmId == nmId)));
}

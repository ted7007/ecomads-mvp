using Ecomads.WebApplication.Services.Wb;

namespace Ecomads.WebApplication.Services.Recommendations;

public static class RecommendationPeriods
{
    public const int Version = 3;
    public static RecommendationWindow Economic(DateTime utc) => Economic(WbSyncPlanner.Today(utc));
    public static RecommendationWindow Economic(DateOnly today) => new(today.AddDays(-15), today.AddDays(-2));
    public static RecommendationWindow Recent(DateOnly today, int days) => new(today.AddDays(-days), today.AddDays(-1));
}

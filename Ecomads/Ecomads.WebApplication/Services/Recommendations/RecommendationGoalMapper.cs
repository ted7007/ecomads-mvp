using Ecomads.WebApplication.Models.Recommendations;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IRecommendationGoalMapper
{
    RecommendationGoal Map(string? goal);
}

public sealed class RecommendationGoalMapper : IRecommendationGoalMapper
{
    public RecommendationGoal Map(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
        {
            return RecommendationGoal.IncreaseRevenue;
        }

        var normalized = Normalize(goal);

        if (ContainsAny(normalized, "снижение дрр", "оптимизация дрр", "reduce drr", "drr reduction", "дрр"))
        {
            return RecommendationGoal.ReduceDrr;
        }

        if (ContainsAny(normalized, "увеличение заказов", "рост заказов", "increase orders", "orders"))
        {
            return RecommendationGoal.IncreaseOrders;
        }

        if (ContainsAny(normalized, "распродажа остатков", "распродаж", "остат", "sell out stock", "sellout"))
        {
            return RecommendationGoal.SellOutStock;
        }

        if (ContainsAny(normalized, "удержание позиций", "позици", "maintain position", "position"))
        {
            return RecommendationGoal.MaintainPosition;
        }

        if (ContainsAny(normalized, "рост выручки", "рост прибыли", "increase revenue", "revenue", "profit"))
        {
            return RecommendationGoal.IncreaseRevenue;
        }

        return RecommendationGoal.IncreaseRevenue;
    }

    private static string Normalize(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace('ё', 'е');
    }

    private static bool ContainsAny(string value, params string[] patterns)
    {
        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

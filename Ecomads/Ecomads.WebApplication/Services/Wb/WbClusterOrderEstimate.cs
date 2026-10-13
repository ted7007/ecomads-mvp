using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbClusterOrderEstimate(decimal? AverageOrderedUnitPrice,
    decimal? EstimatedOrderSum, decimal? EstimatedDrr, string Status, string? Reason);

public static class WbClusterOrderEstimateCalculator
{
    public static WbClusterOrderEstimate Calculate(DateOnly start, DateOnly end,
        IReadOnlyList<CampaignNomenclatureStatistics> articleRows,
        IReadOnlyList<WbClusterStatistic> clusterRows, bool fullStatsComplete, bool clustersComplete,
        bool preliminary)
    {
        var exact = CalculateExact(start, end, articleRows, clusterRows, fullStatsComplete, clustersComplete, preliminary);
        return exact with
        {
            EstimatedOrderSum = exact.EstimatedOrderSum.HasValue ? Math.Round(exact.EstimatedOrderSum.Value, 2) : null,
            EstimatedDrr = exact.EstimatedDrr.HasValue ? Math.Round(exact.EstimatedDrr.Value, 2) : null
        };
    }

    // Shared aggregation for the table adapter and rules. No rounding before comparisons.
    public static WbClusterOrderEstimate CalculateExact(DateOnly start, DateOnly end,
        IReadOnlyList<CampaignNomenclatureStatistics> articleRows,
        IReadOnlyList<WbClusterStatistic> clusterRows, bool fullStatsComplete, bool clustersComplete,
        bool preliminary)
    {
        // Defend against callers passing history or another pair; caller supplies a single campaign/article.
        var basis = articleRows.Where(x => DateOnly.FromDateTime(x.Date) >= start &&
            DateOnly.FromDateTime(x.Date) <= end).ToArray();
        var clusters = clusterRows.Where(x => x.Date >= start && x.Date <= end).ToArray();
        WbClusterOrderEstimate Unavailable(string reason) => new(null, null, null, "unavailable", reason);
        if (!fullStatsComplete) return Unavailable("Fullstats за выбранный период загружен не полностью.");
        if (basis.Length == 0 || basis.Any(x => !x.OrderedProducts.HasValue || x.OrderedProducts < 0))
            return Unavailable("Нет shks товара в fullstats за выбранный период. Нужна повторная загрузка.");
        var units = basis.Sum(x => (decimal)x.OrderedProducts!.Value);
        if (units <= 0 || basis.Any(x => x.Revenue < 0))
            return Unavailable("Среднюю стоимость заказанной единицы за выбранный период рассчитать нельзя.");
        var average = basis.Sum(x => x.Revenue) / units;
        if (clusters.Length == 0 || clusters.Any(x => !x.OrderedProducts.HasValue || x.OrderedProducts < 0))
            return new(average, null, null, "unavailable", "Данные shks кластера неполные.");
        var sum = average * clusters.Sum(x => (decimal)x.OrderedProducts!.Value);
        var drr = sum > 0 ? clusters.Sum(x => x.Spend) / sum * 100m : (decimal?)null;
        var incomplete = !clustersComplete || preliminary;
        return new(average, sum, drr,
            incomplete ? "preliminary" : "complete", !clustersComplete
                ? "Кластеры за выбранный период загружены не полностью." : preliminary
                    ? "Текущий день ещё не завершён." : null);
    }
}

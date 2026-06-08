using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models.Recommendations;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IRecommendationMetricCalculationService
{
    CalculatedKeywordMetrics CalculateKeywordMetrics(KeywordStatistics statistics);
    CalculatedCampaignMetrics CalculateCampaignMetrics(
        CompaignStatistics statistics,
        IReadOnlyCollection<CalculatedKeywordMetrics>? keywordMetrics = null);
}

public sealed class MetricCalculationService : IRecommendationMetricCalculationService
{
    public CalculatedKeywordMetrics CalculateKeywordMetrics(KeywordStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        var periodDays = GetPeriodDays(statistics.StartDate, statistics.EndDate);
        var spend = statistics.Spend;
        var revenue = statistics.Revenue;
        var clicks = ToDecimal(statistics.Clicks);
        var impressions = ToDecimal(statistics.Impressions);
        var orders = ToDecimal(statistics.Orders);

        return new CalculatedKeywordMetrics
        {
            KeywordStatisticId = statistics.Id,
            CampaignId = statistics.CompaignId,
            Phrase = statistics.Phrase ?? string.Empty,
            StartDate = statistics.StartDate,
            EndDate = statistics.EndDate,
            PeriodDays = periodDays,
            Frequency = statistics.Frequency,
            Cpm = statistics.Cpm,
            AvgPosition = statistics.AvgPosition,
            Impressions = statistics.Impressions,
            Clicks = statistics.Clicks,
            Spend = spend,
            Orders = statistics.Orders,
            Revenue = revenue,
            ImportedCtr = ToDecimal(statistics.Ctr),
            ImportedDrr = ToDecimal(statistics.Drr),
            Drr = Percentage(spend, revenue),
            Ctr = Percentage(clicks, impressions),
            Cpc = Divide(spend, clicks),
            Cr = Percentage(orders, clicks),
            Cpo = Divide(spend, orders),
            AverageOrderValue = Divide(revenue, orders),
            AvgDailyOrders = Divide(orders, periodDays)
        };
    }

    public CalculatedCampaignMetrics CalculateCampaignMetrics(
        CompaignStatistics statistics,
        IReadOnlyCollection<CalculatedKeywordMetrics>? keywordMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        var periodDays = GetPeriodDays(statistics.StartDate, statistics.EndDate);
        var spend = ToDecimal(statistics.Spend);
        var revenue = ToDecimal(statistics.Revenue);
        var clicks = ToDecimal(statistics.Clicks);
        var impressions = SumNullable(keywordMetrics?.Select(metric => metric.Impressions));
        var orders = SumNullable(keywordMetrics?.Select(metric => metric.Orders));
        var ordersDecimal = ToDecimal(orders);

        return new CalculatedCampaignMetrics
        {
            CampaignId = statistics.CompaignId,
            StartDate = statistics.StartDate,
            EndDate = statistics.EndDate,
            PeriodDays = periodDays,
            Spend = spend,
            Revenue = revenue,
            Clicks = clicks,
            Impressions = impressions,
            Orders = orders,
            ImportedCtr = ToDecimal(statistics.Ctr),
            ImportedDrr = ToDecimal(statistics.Drr),
            Drr = Percentage(spend, revenue),
            Ctr = Percentage(clicks, ToDecimal(impressions)),
            Cpc = Divide(spend, clicks),
            Cr = Percentage(ordersDecimal, clicks),
            Cpo = Divide(spend, ordersDecimal),
            AverageOrderValue = Divide(revenue, ordersDecimal),
            AvgDailyOrders = Divide(ordersDecimal, periodDays)
        };
    }

    private static decimal? Divide(decimal? numerator, decimal? denominator)
    {
        if (!numerator.HasValue || !denominator.HasValue || denominator.Value == 0m)
        {
            return null;
        }

        return numerator.Value / denominator.Value;
    }

    private static decimal? Percentage(decimal? numerator, decimal? denominator)
    {
        var result = Divide(numerator, denominator);
        return result.HasValue ? result.Value * 100m : null;
    }

    private static int GetPeriodDays(DateTime startDate, DateTime endDate)
    {
        var days = (endDate.Date - startDate.Date).Days + 1;
        return Math.Max(1, days);
    }

    private static decimal ToDecimal(float value)
    {
        return Convert.ToDecimal(value);
    }

    private static decimal? ToDecimal(double? value)
    {
        return value.HasValue ? Convert.ToDecimal(value.Value) : null;
    }

    private static decimal? ToDecimal(int? value)
    {
        return value.HasValue ? value.Value : null;
    }

    private static int? SumNullable(IEnumerable<int?>? values)
    {
        if (values == null)
        {
            return null;
        }

        var materialized = values.ToList();
        return materialized.Any(value => value.HasValue)
            ? materialized.Sum(value => value ?? 0)
            : null;
    }
}

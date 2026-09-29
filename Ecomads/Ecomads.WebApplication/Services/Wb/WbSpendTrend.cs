using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbSpendTrend(DateOnly EndDate, string Status, int LoadedDays,
    decimal? YesterdaySpend, decimal? BaselineDailySpend, decimal? ChangePercent,
    decimal ThresholdPercent, int StoreNormVersion, int CampaignNormVersion);

public static class WbSpendTrendCalculator
{
    public static WbSpendTrend Calculate(DateOnly endDate, IReadOnlyList<CampaignStatistics> rows,
        decimal thresholdPercent, int storeNormVersion, int campaignNormVersion)
    {
        var dates = rows.Select(x => DateOnly.FromDateTime(x.Date)).ToHashSet();
        var required = Enumerable.Range(0, 8).Select(offset => endDate.AddDays(-offset));
        if (required.Any(date => !dates.Contains(date)))
            return new WbSpendTrend(endDate, "insufficient", dates.Count, null, null, null,
                thresholdPercent, storeNormVersion, campaignNormVersion);

        var yesterdaySpend = rows.Single(x => DateOnly.FromDateTime(x.Date) == endDate).Spend;
        var baseline = rows.Where(x => DateOnly.FromDateTime(x.Date) >= endDate.AddDays(-7) &&
                DateOnly.FromDateTime(x.Date) < endDate).Sum(x => x.Spend) / 7m;
        if (baseline == 0)
            return new WbSpendTrend(endDate, "no_baseline", 8, yesterdaySpend, baseline, null,
                thresholdPercent, storeNormVersion, campaignNormVersion);

        var change = Math.Round((yesterdaySpend / baseline - 1m) * 100m, 2);
        var status = change >= thresholdPercent ? "increase" :
            change <= -thresholdPercent ? "decrease" : "normal";
        return new WbSpendTrend(endDate, status, 8, yesterdaySpend, Math.Round(baseline, 2), change,
            thresholdPercent, storeNormVersion, campaignNormVersion);
    }
}

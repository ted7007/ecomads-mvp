namespace Ecomads.WebApplication.Models.Recommendations;

public sealed class CalculatedCampaignMetrics
{
    public Guid CampaignId { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int PeriodDays { get; init; }

    public decimal Spend { get; init; }
    public decimal Revenue { get; init; }
    public decimal Clicks { get; init; }
    public int? Impressions { get; init; }
    public int? Orders { get; init; }

    public decimal? ImportedCtr { get; init; }
    public decimal? ImportedDrr { get; init; }

    public decimal? Drr { get; init; }
    public decimal? Ctr { get; init; }
    public decimal? Cpc { get; init; }
    public decimal? Cr { get; init; }
    public decimal? Cpo { get; init; }
    public decimal? AverageOrderValue { get; init; }
    public decimal? AvgDailyOrders { get; init; }
}

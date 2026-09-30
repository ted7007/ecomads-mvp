namespace Ecomads.WebApplication.Models;

public record ProjectKpiDto(
    decimal Spend,
    decimal Revenue,
    decimal? OrderedAmount,
    decimal Drr,
    int Clicks,
    int Impressions,
    decimal Ctr,
    int CoverageDays,
    int ExpectedDays
);

public record ProjectDashboardDto(
    Guid Id,
    string Name,
    ProjectKpiDto Kpi
)
{
    public decimal TargetDrr { get; init; } = 30m;
    public string? Goal { get; init; }
    public int? WbStatus { get; init; }
}

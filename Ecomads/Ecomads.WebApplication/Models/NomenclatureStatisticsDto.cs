namespace Ecomads.WebApplication.Models;

public sealed class NomenclatureStatisticsDto
{
    public string NomenclatureId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Impressions { get; init; }
    public int Clicks { get; init; }
    public int Carts { get; init; }
    public int Orders { get; init; }
    public decimal Spend { get; init; }
    public decimal Revenue { get; init; }
    public decimal? Ctr { get; init; }
    public decimal? Cr { get; init; }
    public decimal? Cpc { get; init; }
    public decimal? Cpo { get; init; }
    public decimal? TotalOrderSum { get; init; }
    public decimal? TotalAdvertisingSpend { get; init; }
    public decimal? TotalDrr { get; init; }
    public int TotalOrderDays { get; init; }
    public int TotalAdvertisingDays { get; init; }
    public int TotalExpectedDays { get; init; }
    public string TotalStatus { get; init; } = "unavailable";
    public string TotalReason { get; init; } = "Выберите начало и конец периода.";
}

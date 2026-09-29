namespace Ecomads.WebApplication.Data.Models;

public sealed class WbClusterStatistic
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid NomenclatureId { get; set; }
    public DateOnly Date { get; set; }
    public string ClusterName { get; set; } = string.Empty;
    public decimal Spend { get; set; }
    public int? Views { get; set; }
    public int? Clicks { get; set; }
    public int? Carts { get; set; }
    public int? Orders { get; set; }
    public int? OrderedProducts { get; set; }
    public decimal? AveragePosition { get; set; }
    public decimal? Cpc { get; set; }
    public decimal? Cpm { get; set; }
    public decimal? Ctr { get; set; }
}

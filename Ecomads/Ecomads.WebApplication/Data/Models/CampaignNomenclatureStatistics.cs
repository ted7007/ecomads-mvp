namespace Ecomads.WebApplication.Data.Models;

public class CampaignNomenclatureStatistics
{
    public Guid CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    public Guid NomenclatureId { get; set; }
    public Nomenclature Nomenclature { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal Spend { get; set; }
    public decimal Revenue { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public int Carts { get; set; }
    public int Orders { get; set; }
    public int Cancellations { get; set; }
    public decimal? Ctr { get; set; }
    public decimal? Cr { get; set; }
    public decimal? Cpm { get; set; }
    public decimal? Cpc { get; set; }
    public decimal? Cpo { get; set; }
    public decimal? AveragePosition { get; set; }
}

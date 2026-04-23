using System.ComponentModel.DataAnnotations;

namespace Ecomads.WebApplication.Data.Models;

public class KeywordStatistics
{
    [Key]
    public Guid Id { get; set; }
    
    public Guid CompaignId { get; set; }
    public Compaign Compaign { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public string Phrase { get; set; } = null!;
    public int? Frequency { get; set; }
    public decimal? Cpm { get; set; }
    public double? AvgPosition { get; set; }
    public int? Impressions { get; set; }
    public int? Clicks { get; set; }
    public double? Ctr { get; set; }
    public decimal? Spend { get; set; }
    public int? Orders { get; set; }
    public decimal? Revenue { get; set; }
    public double? Drr { get; set; }
}

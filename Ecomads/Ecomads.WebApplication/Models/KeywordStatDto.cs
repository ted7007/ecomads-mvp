namespace Ecomads.WebApplication.Data.Models;

public class KeywordStatDto
{
    public string Phrase { get; set; }
    public Guid CompaignId { get; set; }
    public DateTime Datetime { get; set; }
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
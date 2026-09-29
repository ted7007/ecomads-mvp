
namespace Ecomads.WebApplication.Data.Models;

public class CampaignStatistics
{
    public Guid CampaignId { get; set; }
    
    public DateTime Date { get; set; }

    public decimal Revenue { get; set; }
    public decimal Spend { get; set; }
    public int Clicks { get; set; }
    public decimal Ctr { get; set; }
    public decimal Drr { get; set; }

    public int Impressions { get; set; }
    public int Carts { get; set; }
    public int Orders { get; set; }
    public int Cancellations { get; set; }
}


namespace Ecomads.WebApplication.Data.Models;

public class CampaignStatistics
{
    public Guid CampaignId { get; set; }
    
    public DateTime StartDate { get; set; }
    
    public DateTime EndDate { get; set; }

    public CampaignStatisticsType Type { get; set; } = CampaignStatisticsType.General;
    
    public float Revenue { get; set; }
    public float Spend { get; set; }
    public float Clicks { get; set; }
    public float Ctr { get; set; }
    public float Drr { get; set; }

    public int Impressions { get; set; }
    public int Carts { get; set; }
    public int Orders { get; set; }
    public int Cancellations { get; set; }
}


namespace Ecomads.WebApplication.Data.Models;

public class CompaignStatistics
{
    public Guid CompaignId { get; set; }
    
    public DateTime StartDate { get; set; }
    
    public DateTime EndDate { get; set; }

    public CompaignStatisticsType Type { get; set; } = CompaignStatisticsType.General;
    
    public float Revenue { get; set; }
    public float Spend { get; set; }
    public float Clicks { get; set; }
    public float Ctr { get; set; }
    public float Drr { get; set; }
}
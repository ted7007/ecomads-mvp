namespace Ecomads.WebApplication.Data.Models;

public sealed class WbCampaignDailyCheck
{
    public Guid CampaignId { get; set; }
    public DateOnly Date { get; set; }
    public Guid? JobId { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public string Result { get; set; } = "unknown";
    public decimal? Spend { get; set; }
}

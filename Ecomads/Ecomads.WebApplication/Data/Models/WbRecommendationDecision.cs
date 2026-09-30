namespace Ecomads.WebApplication.Data.Models;

public sealed class WbRecommendationDecision
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public string RecommendationKey { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

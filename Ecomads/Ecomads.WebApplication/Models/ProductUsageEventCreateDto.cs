namespace Ecomads.WebApplication.Models;

public class ProductUsageEventCreateDto
{
    public Guid? UserId { get; set; }

    public string EventName { get; set; } = null!;

    public string FeatureName { get; set; } = null!;

    public Guid? CampaignId { get; set; }

    public Guid? KeywordId { get; set; }

    public Guid? LlmUsageId { get; set; }

    public object? Metadata { get; set; }

    public string? Path { get; set; }

    public string? Method { get; set; }

    public string? UserAgent { get; set; }

    public string? IpHash { get; set; }
}

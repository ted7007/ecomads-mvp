namespace Ecomads.WebApplication.Data.Models;

public class LlmUsageEvent
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? KeywordId { get; set; }

    public string Provider { get; set; } = null!;

    public string Model { get; set; } = null!;

    public string OperationName { get; set; } = null!;

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int? TotalTokens { get; set; }

    public decimal? BothubCaps { get; set; }

    public decimal? EstimatedCostRub { get; set; }

    public bool IsSuccess { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public long DurationMs { get; set; }

    public string? RequestMetadataJson { get; set; }

    public string? ResponseMetadataJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

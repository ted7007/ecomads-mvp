namespace Ecomads.WebApplication.Models;

public class LlmUsageSuccessDto
{
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

    public int? HttpStatusCode { get; set; }

    public long DurationMs { get; set; }

    public object? RequestMetadata { get; set; }

    public object? ResponseMetadata { get; set; }
}

public class LlmUsageFailureDto
{
    public Guid? UserId { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? KeywordId { get; set; }

    public string Provider { get; set; } = null!;

    public string Model { get; set; } = null!;

    public string OperationName { get; set; } = null!;

    public int? HttpStatusCode { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public long DurationMs { get; set; }

    public object? RequestMetadata { get; set; }

    public object? ResponseMetadata { get; set; }
}

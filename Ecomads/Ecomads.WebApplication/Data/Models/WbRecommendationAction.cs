namespace Ecomads.WebApplication.Data.Models;

// Latest source status is separate from the immutable recommendation evidence.
public sealed class WbAdvertisingSettings
{
    public Guid CampaignId { get; set; }
    public string Status { get; set; } = "unknown";
    public string? BidType { get; set; }
    public string? PaymentType { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime? ReceivedAtUtc { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
}

public sealed class WbSnapshotDecision
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid EvaluationId { get; set; }
    public string RuleId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateOnly? SnoozeUntil { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

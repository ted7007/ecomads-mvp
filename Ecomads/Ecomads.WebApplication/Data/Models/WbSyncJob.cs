namespace Ecomads.WebApplication.Data.Models;

public sealed class WbSyncJob
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string CampaignIdsJson { get; set; } = "[]";
    public string? PairIdsJson { get; set; }
    public string Kind { get; set; } = "fullstats";
    public int NextCampaignOffset { get; set; }
    public int AttemptCount { get; set; }
    public string Status { get; set; } = "pending";
    public string Stage { get; set; } = "queued";
    public string? WaitReason { get; set; }
    public Guid? RetriedFromJobId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastRequestAtUtc { get; set; }
    public string? ErrorCode { get; set; }
}

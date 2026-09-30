namespace Ecomads.WebApplication.Data.Models;

public sealed class WbSyncJobEvent
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Stage { get; set; } = "queued";
    public string? ErrorCode { get; set; }
    public int ProcessedCount { get; set; }
    public int AttemptNumber { get; set; }
}

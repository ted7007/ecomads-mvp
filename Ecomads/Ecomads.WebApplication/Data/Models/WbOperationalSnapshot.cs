namespace Ecomads.WebApplication.Data.Models;

/// <summary>Immutable source evidence. Failed attempts supersede old successful data for actions.</summary>
public sealed class WbOperationalSnapshot
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public string Source { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public string State { get; set; } = "unknown";
    public string? ErrorCode { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? CoverageStartedAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}

namespace Ecomads.WebApplication.Data.Models;

public static class WbIngestionVersion
{
    public const string Current = "3";
}

public sealed class WbResponseSnapshot
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid? JobId { get; set; }
    public string Kind { get; set; } = "";
    public string RequestUri { get; set; } = "";
    public string? RequestBody { get; set; }
    public string JobStateJson { get; set; } = "{}";
    public int Attempt { get; set; }
    public int Page { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public int? HttpStatus { get; set; }
    public string Body { get; set; } = "";
    public string? RawBodyBase64 { get; set; }
    public byte[] GetBodyBytes() => RawBodyBase64 == null ? System.Text.Encoding.UTF8.GetBytes(Body) : Convert.FromBase64String(RawBodyBase64);
    public string Sha256 { get; set; } = "";
    public string Version { get; set; } = WbIngestionVersion.Current;
    public string State { get; set; } = "received";
    public string? Reason { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public int ProductOrdersVersion { get; set; }
    public string? ProductOrdersError { get; set; }
}

public sealed class WbSourceQuality
{
    public Guid StoreId { get; set; }
    public string Source { get; set; } = "";
    public string Entity { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string State { get; set; } = "legacy";
    public string? Reason { get; set; }
    public DateTime LastAttemptAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid? SnapshotId { get; set; }
    public Guid? AcceptedSnapshotId { get; set; }
    public decimal? PreviousValue { get; set; }
    public decimal? ProposedValue { get; set; }
}

public sealed class WbCostObservation
{
    public Guid SnapshotId { get; set; }
    public int Ordinal { get; set; }
    public Guid StoreId { get; set; }
    public string RawJson { get; set; } = "";
    public string? RawTime { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public DateOnly? MoscowDate { get; set; }
    public decimal? Amount { get; set; }
    public string State { get; set; } = "";
}

public sealed class WbIngestionDecision
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid SellerId { get; set; }
    public Guid SnapshotId { get; set; }
    public DateOnly Date { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal AcceptedValue { get; set; }
    public DateTime DecidedAtUtc { get; set; }
}

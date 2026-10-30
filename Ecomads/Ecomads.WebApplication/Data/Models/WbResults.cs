namespace Ecomads.WebApplication.Data.Models;

public sealed class WbResultObservation
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public string State { get; set; } = "unknown";
    public string PayloadJson { get; set; } = "{}";
}

public sealed class WbResultRecommendation
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public Guid StoreId { get; set; }
    public Guid CampaignId { get; set; }
    public string SubjectKey { get; set; } = "";
    public DateTime FirstShownAtUtc { get; set; }
    public DateTime LastShownAtUtc { get; set; }
    public string CampaignName { get; set; } = "";
    public Guid? MeasurementId { get; set; }
    public string? BaselineJson { get; set; }
    public bool Legacy { get; set; }
    public DateOnly? LastConditionDate { get; set; }
    public int ClearConditionNights { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public DateTime? OtherChangeAtUtc { get; set; }
}

// One immutable card per displayed calculation, rather than rewriting the evidence of a decision.
public sealed class WbResultExposure
{
    public Guid Id { get; set; }
    public Guid RecommendationId { get; set; }
    public Guid RunId { get; set; }
    public Guid EvaluationId { get; set; }
    public string RuleId { get; set; } = "";
    public DateTime ShownAtUtc { get; set; }
    public string CardJson { get; set; } = "{}";
}

public sealed class WbResultMeasurement
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public Guid StoreId { get; set; }
    public Guid CampaignId { get; set; }
    public DateOnly DetectedDate { get; set; }
    public RecommendationPeriod Before { get; set; } = new();
    public RecommendationPeriod After { get; set; } = new();
    public string MembersJson { get; set; } = "[]";
    public string Status { get; set; } = "measuring";
    public string Reason { get; set; } = "Идёт семидневный замер";
    public decimal? Savings { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? InterruptedAtUtc { get; set; }
    public int Revision { get; set; }
    public bool ActionPreserved { get; set; }
    public DateTime? ContinuationCheckedAtUtc { get; set; }
    public string? ContinuationReason { get; set; }
}

public sealed class RecommendationPeriod
{
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
}

public sealed class WbResultRevision
{
    public Guid Id { get; set; }
    public Guid MeasurementId { get; set; }
    public int Number { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTime CalculatedAtUtc { get; set; }
    public string EvidenceJson { get; set; } = "{}";
}

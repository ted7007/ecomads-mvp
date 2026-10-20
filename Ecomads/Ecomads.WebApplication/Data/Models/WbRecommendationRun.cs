namespace Ecomads.WebApplication.Data.Models;

/// <summary>Immutable calculation evidence; user decisions and action measurements are added in later sprints.</summary>
public sealed class WbRecommendationRun
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public Guid StoreId { get; set; }
    public Guid CampaignId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Fingerprint { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public DateTime CalculatedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class WbArticleObservation
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid NomenclatureId { get; set; }
    public DateOnly Date { get; set; }
    public Guid? JobId { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public int Orders { get; set; }
    public int? OrderedProducts { get; set; }
    public decimal Revenue { get; set; }
}

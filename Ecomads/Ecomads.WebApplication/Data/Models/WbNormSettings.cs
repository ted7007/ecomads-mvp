namespace Ecomads.WebApplication.Data.Models;

public sealed class WbStoreNorms
{
    public Guid StoreId { get; set; }
    public decimal TargetDrr { get; set; } = 30m;
    public int MinClicks { get; set; } = 30;
    public decimal MinSpend { get; set; } = 500m;
    public int MinOrders { get; set; } = 3;
    public decimal DeviationPercent { get; set; } = 40m;
    public int Version { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WbCampaignNorms
{
    public Guid CampaignId { get; set; }
    public string? CustomName { get; set; }
    public string? Goal { get; set; }
    public decimal? TargetDrr { get; set; }
    public int? MinClicks { get; set; }
    public decimal? MinSpend { get; set; }
    public int? MinOrders { get; set; }
    public decimal? DeviationPercent { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WbNormRevision
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid? CampaignId { get; set; }
    public int Version { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

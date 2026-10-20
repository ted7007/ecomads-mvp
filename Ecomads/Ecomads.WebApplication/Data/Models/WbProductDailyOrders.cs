namespace Ecomads.WebApplication.Data.Models;

// A published day is a complete catalogue snapshot, never a partially loaded page.
public sealed class WbProductOrderDay
{
    public Guid StoreId { get; set; }
    public DateOnly Date { get; set; }
    public Guid SnapshotId { get; set; }
    public DateTime LoadedAtUtc { get; set; }
    public int ProductCount { get; set; }
}

public sealed class WbProductDailyOrders
{
    public Guid StoreId { get; set; }
    public DateOnly Date { get; set; }
    public long NmId { get; set; }
    public decimal OrderSum { get; set; }
    public int OrderCount { get; set; }
}

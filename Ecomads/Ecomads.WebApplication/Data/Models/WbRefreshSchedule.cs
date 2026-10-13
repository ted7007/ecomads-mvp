namespace Ecomads.WebApplication.Data.Models;

// One durable, coalescing request per store and task; old missed slots never accumulate.
public sealed class WbRefreshSchedule
{
    public Guid StoreId { get; set; }
    public string Task { get; set; } = string.Empty;
    public DateTime RequestedSlotUtc { get; set; }
    public DateTime? PlannedSlotUtc { get; set; }
}

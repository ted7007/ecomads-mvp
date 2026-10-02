namespace Ecomads.WebApplication.Data.Models;

public sealed class WbMethodSlot
{
    public Guid StoreId { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime LastRequestAtUtc { get; set; }
}

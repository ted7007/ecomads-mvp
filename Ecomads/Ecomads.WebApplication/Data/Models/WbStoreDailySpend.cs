namespace Ecomads.WebApplication.Data.Models;

public sealed class WbStoreDailySpend
{
    public Guid StoreId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Spend { get; set; }
    public DateTime LoadedAtUtc { get; set; }
}

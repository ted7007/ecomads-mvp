namespace Ecomads.WebApplication.Data.Models;

public sealed class WbStoreDailyOrders
{
    public Guid StoreId { get; set; }
    public DateOnly Date { get; set; }
    public int OrderCount { get; set; }
    public decimal OrderSum { get; set; }
    public int OpenCount { get; set; }
    public int CartCount { get; set; }
    public int BuyoutCount { get; set; }
    public decimal BuyoutSum { get; set; }
    public string Source { get; set; } = "history";
    public DateTime LoadedAtUtc { get; set; }
}

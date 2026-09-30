namespace Ecomads.WebApplication.Data.Models;

public sealed class WbJamSearchQuery
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid NomenclatureId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SearchText { get; set; } = string.Empty;
    public long? Frequency { get; set; }
    public long? WeekFrequency { get; set; }
    public decimal? AveragePosition { get; set; }
    public decimal? MedianPosition { get; set; }
    public long? OpenCard { get; set; }
    public long? AddToCart { get; set; }
    public long? Orders { get; set; }
    public DateTime LoadedAtUtc { get; set; }
}

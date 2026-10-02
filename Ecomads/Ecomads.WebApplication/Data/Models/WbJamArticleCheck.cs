namespace Ecomads.WebApplication.Data.Models;

public sealed class WbJamArticleCheck
{
    public Guid StoreId { get; set; }
    public Guid NomenclatureId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public Guid? JobId { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public bool HasData { get; set; }
}

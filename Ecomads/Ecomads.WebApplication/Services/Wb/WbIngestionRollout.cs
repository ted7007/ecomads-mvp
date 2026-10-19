namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbIngestionRollout(IConfiguration configuration)
{
    // Empty means unrestricted. An invalid nonempty list deliberately enables no stores.
    public Guid[]? StoreIds
    {
        get
        {
            var text = configuration["Wb:Ingestion:StoreIds"];
            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x =>
                Guid.TryParse(x.Trim(), out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
        }
    }
    public bool Allows(Guid storeId) => StoreIds is not { } ids || ids.Contains(storeId);
}

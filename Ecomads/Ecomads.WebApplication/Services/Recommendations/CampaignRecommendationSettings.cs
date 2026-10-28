using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public static class CampaignRecommendationSettings
{
    public const string AutoReplenishmentReason = "budget_auto_replenishment";
    public const string AutoReplenishmentLabel = "Включено автопополнение";

    // Both callers save the setting/revision as part of their own atomic transaction.
    public static async Task<WbCampaignNorms> GetOrCreateAsync(EcomadsDbContext db, Guid campaignId, CancellationToken ct)
    {
        var settings = await db.WbCampaignNorms.SingleOrDefaultAsync(x => x.CampaignId == campaignId, ct);
        if (settings != null) { settings.Version++; return settings; }
        settings = new() { CampaignId = campaignId };
        db.WbCampaignNorms.Add(settings);
        return settings;
    }

    public static void SetAutoReplenishment(WbCampaignNorms settings, bool? enabled)
    {
        if (enabled.HasValue) settings.BudgetAutoReplenishmentEnabled = enabled.Value;
    }

    public static void RecordRevision(EcomadsDbContext db, Guid storeId, WbCampaignNorms settings)
    {
        settings.UpdatedAtUtc = DateTime.UtcNow;
        db.WbNormRevisions.Add(new() { Id = Guid.NewGuid(), StoreId = storeId, CampaignId = settings.CampaignId,
            Version = settings.Version, SettingsJson = JsonSerializer.Serialize(settings), CreatedAtUtc = settings.UpdatedAtUtc });
    }
}

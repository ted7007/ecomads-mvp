using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

public static class WbCampaignEligibility
{
    public static bool FinishedBefore(Campaign campaign, DateOnly periodStart)
    {
        if (campaign.WbStatus != 7) return false;
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(periodStart.ToDateTime(TimeOnly.MinValue), moscow);
        // WB's "2100-01-01 +03:00" sentinel is 2099-12-31 21:00 UTC.
        if (campaign.WbDeletedAtUtc is { Year: < 2099 } deleted)
            return deleted < startUtc;
        return campaign.WbUpdatedAtUtc < startUtc;
    }
}

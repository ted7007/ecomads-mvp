using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbReportSourceReadiness(string Kind, bool Ready, DateTime? CheckedAtUtc, string? Reason);
public sealed record WbReportMissingCampaign(Guid CampaignId, string WbCampaignId, string Name);
public sealed record WbReportReadiness(DateOnly ReportDate, bool Ready,
    IReadOnlyList<WbReportSourceReadiness> Sources, bool Collected,
    IReadOnlyList<Guid> ConfirmedCampaignIds, IReadOnlyList<WbReportMissingCampaign> MissingCampaigns);

// Collection can finish with sparse WB data. Ready still means fully confirmed, not merely attempted.
public sealed class WbReportReadinessService(EcomadsDbContext db, TimeProvider? clock = null)
{
    public async Task<WbReportReadiness> GetAsync(Guid storeId, DateOnly date, CancellationToken ct)
    {
        var dayClosedAt = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), WbRefreshClock.Moscow);
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == storeId, ct);
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId &&
            (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11)).ToListAsync(ct);
        var expectedIds = campaigns.Where(x => !WbCampaignEligibility.CreatedAfter(x, date) &&
            !WbCampaignEligibility.FinishedBefore(x, date)).Select(x => x.Id).ToArray();
        var checks = await db.WbCampaignDailyChecks.AsNoTracking().Where(x => expectedIds.Contains(x.CampaignId) &&
            x.Date == date).ToListAsync(ct);
        var quality = await db.WbSourceQualities.AsNoTracking().Where(x => x.StoreId == storeId &&
            x.StartDate <= date && x.EndDate >= date).ToArrayAsync(ct);
        WbSourceQuality? Latest(string kind, string entity = "") => quality.Where(x => x.Source == kind &&
            (x.Entity == entity || x.Entity == "")).OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefault();
        bool Accepted(string kind, string entity = "") => Latest(kind, entity) is not { } q || WbQuality.Confirmed(q.State);
        var confirmed = checks.Where(x => x.Result is "data" or "zero" && x.Spend != null &&
            Accepted("fullstats", x.CampaignId.ToString()) &&
            x.CheckedAtUtc >= dayClosedAt &&
            !(x.LastAttemptResult == "no_data" && x.LastAttemptAtUtc >= x.CheckedAtUtc)).ToArray();
        var confirmedIds = confirmed.Select(x => x.CampaignId).ToArray();
        var attempted = expectedIds.Count(id => confirmedIds.Contains(id) ||
            Latest("fullstats", id.ToString())?.LastAttemptAtUtc >= dayClosedAt ||
            checks.Any(x => x.CampaignId == id && x.LastAttemptAtUtc >= dayClosedAt && x.LastAttemptResult == "no_data"));
        var missing = campaigns.Where(x => expectedIds.Contains(x.Id) && !confirmedIds.Contains(x.Id))
            .Select(x => new WbReportMissingCampaign(x.Id, x.WbCampaignId, x.Name)).ToArray();
        var spend = await db.WbStoreDailySpends.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.Date == date, ct);
        var orders = await db.WbStoreDailyOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.Date == date, ct);
        var campaignListQuality = await db.WbSourceQualities.AsNoTracking().Where(x => x.StoreId == storeId &&
            x.Source == "campaign_list" && x.Entity == "").OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefaultAsync(ct);
        var campaignListAttempted = (campaignListQuality?.LastAttemptAtUtc ?? store.CampaignsRefreshedAtUtc) >= dayClosedAt;
        var campaignListReady = campaignListAttempted &&
            (campaignListQuality == null || WbQuality.Confirmed(campaignListQuality.State));
        var sources = new[]
        {
            Source("campaign_list", campaignListReady, campaignListQuality?.LastAttemptAtUtc ?? store.CampaignsRefreshedAtUtc,
                campaignListQuality?.Reason ?? "Список кампаний не подтверждён после завершения дня."),
            Source("fullstats", campaignListReady && confirmed.Length == expectedIds.Length,
                checks.Count == 0 ? store.CampaignsRefreshedAtUtc : checks.Min(x => x.LastAttemptAtUtc ?? x.CheckedAtUtc),
                !campaignListReady ? "Список кампаний не проверен после завершения дня." :
                $"WB не подтвердил данные {missing.Length} из {expectedIds.Length} кампаний за отчётный день."),
            Source("expenses", spend?.LoadedAtUtc >= dayClosedAt && Accepted("expenses"), Latest("expenses")?.LastAttemptAtUtc ?? spend?.LoadedAtUtc,
                Latest("expenses")?.Reason ?? "Расход кабинета не подтверждён после завершения дня."),
            Source("orders", orders?.LoadedAtUtc >= dayClosedAt && Accepted("orders"), Latest("orders")?.LastAttemptAtUtc ?? orders?.LoadedAtUtc,
                Latest("orders")?.Reason ?? "Все заказы кабинета не подтверждены после завершения дня.")
        };
        var collected = dayClosedAt <= (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime && campaignListAttempted && attempted == expectedIds.Length &&
            sources.Where(x => x.Kind != "fullstats").All(x => x.CheckedAtUtc >= dayClosedAt);
        return new(date, collected && sources.All(x => x.Ready), sources, collected, confirmedIds, missing);
    }

    private static WbReportSourceReadiness Source(string kind, bool ready, DateTime? at, string reason) =>
        new(kind, ready, at, ready ? null : reason);
}

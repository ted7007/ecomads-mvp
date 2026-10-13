using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbReportSourceReadiness(string Kind, bool Ready, DateTime? CheckedAtUtc, string? Reason);
public sealed record WbReportMissingCampaign(Guid CampaignId, string WbCampaignId, string Name);
public sealed record WbReportReadiness(DateOnly ReportDate, bool Ready,
    IReadOnlyList<WbReportSourceReadiness> Sources, bool Collected,
    IReadOnlyList<Guid> ConfirmedCampaignIds, IReadOnlyList<WbReportMissingCampaign> MissingCampaigns);

// Collection can finish with sparse WB data. Ready still means fully confirmed, not merely attempted.
public sealed class WbReportReadinessService(EcomadsDbContext db)
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
        var confirmed = checks.Where(x => x.Result is "data" or "zero" && x.Spend != null &&
            x.CheckedAtUtc >= dayClosedAt &&
            !(x.LastAttemptResult == "no_data" && x.LastAttemptAtUtc >= x.CheckedAtUtc)).ToArray();
        var confirmedIds = confirmed.Select(x => x.CampaignId).ToArray();
        var attempted = checks.Count(x => confirmedIds.Contains(x.CampaignId) ||
            (x.LastAttemptAtUtc >= dayClosedAt && x.LastAttemptResult == "no_data"));
        var missing = campaigns.Where(x => expectedIds.Contains(x.Id) && !confirmedIds.Contains(x.Id))
            .Select(x => new WbReportMissingCampaign(x.Id, x.WbCampaignId, x.Name)).ToArray();
        var spend = await db.WbStoreDailySpends.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.Date == date, ct);
        var orders = await db.WbStoreDailyOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.Date == date, ct);
        var campaignListReady = store.CampaignsRefreshedAtUtc >= dayClosedAt;
        var sources = new[]
        {
            Source("fullstats", campaignListReady && confirmed.Length == expectedIds.Length,
                checks.Count == 0 ? store.CampaignsRefreshedAtUtc : checks.Min(x => x.LastAttemptAtUtc ?? x.CheckedAtUtc),
                !campaignListReady ? "Список кампаний не проверен после завершения дня." :
                $"WB не подтвердил данные {missing.Length} из {expectedIds.Length} кампаний за отчётный день."),
            Source("expenses", spend?.LoadedAtUtc >= dayClosedAt, spend?.LoadedAtUtc, "Расход кабинета не проверен после завершения дня."),
            Source("orders", orders?.LoadedAtUtc >= dayClosedAt, orders?.LoadedAtUtc, "Все заказы кабинета не проверены после завершения дня.")
        };
        var collected = dayClosedAt <= DateTime.UtcNow && campaignListReady && attempted == expectedIds.Length &&
            sources.Where(x => x.Kind != "fullstats").All(x => x.Ready);
        return new(date, collected && sources.All(x => x.Ready), sources, collected, confirmedIds, missing);
    }

    private static WbReportSourceReadiness Source(string kind, bool ready, DateTime? at, string reason) =>
        new(kind, ready, at, ready ? null : reason);
}

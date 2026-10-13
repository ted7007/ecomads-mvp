using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbReportSourceReadiness(string Kind, bool Ready, DateTime? CheckedAtUtc, string? Reason);
public sealed record WbReportReadiness(DateOnly ReportDate, bool Ready,
    IReadOnlyList<WbReportSourceReadiness> Sources);

// Shared prerequisite for future digests. Scheduling and Telegram delivery are separate concerns.
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
            x.Date == date && (x.Result == "data" || x.Result == "zero") && x.Spend != null &&
            x.CheckedAtUtc >= dayClosedAt).Select(x => x.CheckedAtUtc).ToListAsync(ct);
        var spend = await db.WbStoreDailySpends.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.Date == date, ct);
        var orders = await db.WbStoreDailyOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.Date == date, ct);
        var campaignListReady = store.CampaignsRefreshedAtUtc >= dayClosedAt;
        var sources = new[]
        {
            Source("fullstats", campaignListReady && checks.Count == expectedIds.Length,
                checks.Count == 0 ? store.CampaignsRefreshedAtUtc : checks.Min(), "Не подтверждены все кампании за отчётный день."),
            Source("expenses", spend?.LoadedAtUtc >= dayClosedAt, spend?.LoadedAtUtc, "Расход кабинета не проверен после завершения дня."),
            Source("orders", orders?.LoadedAtUtc >= dayClosedAt, orders?.LoadedAtUtc, "Все заказы кабинета не проверены после завершения дня.")
        };
        return new(date, sources.All(x => x.Ready), sources);
    }

    private static WbReportSourceReadiness Source(string kind, bool ready, DateTime? at, string reason) =>
        new(kind, ready, at, ready ? null : reason);
}

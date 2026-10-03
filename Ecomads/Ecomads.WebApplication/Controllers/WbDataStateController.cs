using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/data-state")]
public sealed class WbDataStateController(EcomadsDbContext db, WbDataCoverageService coverage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        [FromQuery] Guid? campaignId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        if (startDate == default || endDate < startDate || endDate > WbSyncPlanner.Yesterday() ||
            endDate.DayNumber - startDate.DayNumber > 365) return BadRequest();
        var stores = campaignId.HasValue
            ? await db.Campaigns.Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
                .Select(x => x.StoreId).ToArrayAsync(ct)
            : await db.Stores.Where(x => x.SellerId == sellerId && x.ApiKey != null)
                .Select(x => x.Id).ToArrayAsync(ct);
        if (campaignId.HasValue && stores.Length == 0) return NotFound();
        var jobs = await db.WbSyncJobs.AsNoTracking().Where(x => stores.Contains(x.StoreId))
            .OrderByDescending(x => x.UpdatedAtUtc).ToListAsync(ct);
        var checkedPeriod = await coverage.GetAsync(sellerId, startDate, endDate, campaignId, ct);
        var allDays = checkedPeriod.Days.Count;
        var spendDays = checkedPeriod.Days.Count(x => x.CheckedCampaigns == x.ExpectedCampaigns && x.ExpectedCampaigns > 0);
        var expenseDays = checkedPeriod.Days.Count(x => x.Spend.HasValue);
        var orderDays = checkedPeriod.Days.Count(x => x.Orders.HasValue);
        var checks = await db.WbCampaignDailyChecks.AsNoTracking().Where(x => x.Date >= startDate &&
                x.Date <= endDate && x.CampaignId != Guid.Empty &&
                db.Campaigns.Any(c => c.Id == x.CampaignId && stores.Contains(c.StoreId) &&
                    (!campaignId.HasValue || c.Id == campaignId.Value)))
            .Select(x => new { x.Date, x.CheckedAtUtc }).ToArrayAsync(ct);
        var orderChecks = await db.WbStoreDailyOrders.AsNoTracking().Where(x => stores.Contains(x.StoreId) &&
                x.Date >= startDate && x.Date <= endDate)
            .Select(x => new { x.Date, x.LoadedAtUtc }).ToArrayAsync(ct);
        var expenseChecks = campaignId.HasValue ? [] : await db.WbStoreDailySpends.AsNoTracking()
            .Where(x => stores.Contains(x.StoreId) && x.Date >= startDate && x.Date <= endDate)
            .Select(x => new { x.Date, x.LoadedAtUtc }).ToArrayAsync(ct);
        var clusterDates = await db.WbClusterStatistics.AsNoTracking().Where(x => x.Date >= startDate &&
                x.Date <= endDate && db.Campaigns.Any(c => c.Id == x.CampaignId && stores.Contains(c.StoreId) &&
                    (!campaignId.HasValue || c.Id == campaignId.Value)))
            .Select(x => x.Date).Distinct().ToArrayAsync(ct);
        var campaignArticleIds = campaignId.HasValue
            ? await db.CampaignNomenclatureStatistics.AsNoTracking()
                .Where(x => x.CampaignId == campaignId.Value)
                .Select(x => x.NomenclatureId).Distinct().ToArrayAsync(ct)
            : [];
        var jamChecks = await db.WbJamArticleChecks.AsNoTracking().Where(x => stores.Contains(x.StoreId) &&
                x.StartDate >= startDate && x.EndDate <= endDate &&
                (!campaignId.HasValue || campaignArticleIds.Contains(x.NomenclatureId)))
            .Select(x => new { x.StartDate, x.EndDate, x.CheckedAtUtc }).ToArrayAsync(ct);

        object Source(string kind, string[] jobKinds, int covered, int expected, DateTime? verifiedAt,
            DateOnly? availableStartDate, DateOnly? availableEndDate)
        {
            var relevant = jobs.Where(x => jobKinds.Contains(x.Kind)).ToArray();
            var active = relevant.FirstOrDefault(x => x.Status is "pending" or "running");
            var latest = relevant.FirstOrDefault();
            var unresolvedFailure = relevant.FirstOrDefault(x => x.Status == "failed" &&
                !relevant.Any(next => next.Kind == x.Kind && next.Status == "completed" &&
                    next.UpdatedAtUtc > x.UpdatedAtUtc));
            var status = active != null ? active.WaitReason == "rate_limit" ? "waiting" : "loading" :
                unresolvedFailure != null ? "failed" : covered == 0 ? "not_loaded" :
                    covered < expected ? "partial" : "complete";
            var version = Math.Max(verifiedAt?.Ticks ?? 0, latest?.UpdatedAtUtc.Ticks ?? 0).ToString();
            return new { kind, status, startDate, endDate, availableStartDate, availableEndDate,
                covered, expected, lastCheckedAtUtc = verifiedAt,
                nextAttemptAtUtc = active?.NextAttemptAtUtc,
                estimatedCompletionAtUtc = active == null ? (DateTime?)null :
                    active.NextAttemptAtUtc.AddTicks(WbRateLimits.IntervalFor(active.Kind).Ticks *
                        Math.Max(0, (WbSyncJobUnits.Total(active) - active.NextCampaignOffset - 1) /
                            WbSyncJobUnits.BatchSize(active.Kind))),
                errorCode = unresolvedFailure?.ErrorCode,
                failedJobKind = unresolvedFailure?.Kind, version };
        }

        var sources = campaignId.HasValue
            ? new[] {
                Source("fullstats", ["fullstats", "archive"], spendDays, allDays,
                    checks.Length == 0 ? null : checks.Max(x => x.CheckedAtUtc),
                    checks.Length == 0 ? null : checks.Min(x => x.Date),
                    checks.Length == 0 ? null : checks.Max(x => x.Date)),
                Source("clusters", ["clusters"], clusterDates.Length > 0 ? 1 : 0, 1,
                    jobs.FirstOrDefault(x => x.Kind == "clusters" && x.Status == "completed")?.CompletedAtUtc,
                    clusterDates.Length == 0 ? null : clusterDates.Min(),
                    clusterDates.Length == 0 ? null : clusterDates.Max()),
                Source("jam", ["jam"], jamChecks.Length > 0 ? 1 : 0, 1,
                    jamChecks.Length == 0 ? null : jamChecks.Max(x => x.CheckedAtUtc),
                    jamChecks.Length == 0 ? null : jamChecks.Min(x => x.StartDate),
                    jamChecks.Length == 0 ? null : jamChecks.Max(x => x.EndDate)) }
            : new[] {
                Source("fullstats", ["fullstats", "archive"], spendDays, allDays,
                    checks.Length == 0 ? null : checks.Max(x => x.CheckedAtUtc),
                    checks.Length == 0 ? null : checks.Min(x => x.Date),
                    checks.Length == 0 ? null : checks.Max(x => x.Date)),
                Source("expenses", ["expenses"], expenseDays, allDays,
                    expenseChecks.Length == 0 ? null : expenseChecks.Max(x => x.LoadedAtUtc),
                    expenseChecks.Length == 0 ? null : expenseChecks.Min(x => x.Date),
                    expenseChecks.Length == 0 ? null : expenseChecks.Max(x => x.Date)),
                Source("orders", ["funnel_recent", "funnel_backfill", "funnel"], orderDays, allDays,
                    orderChecks.Length == 0 ? null : orderChecks.Max(x => x.LoadedAtUtc),
                    orderChecks.Length == 0 ? null : orderChecks.Min(x => x.Date),
                    orderChecks.Length == 0 ? null : orderChecks.Max(x => x.Date)) };
        return Ok(new { sources, active = jobs.Any(x => x.Status is "pending" or "running") });
    }
}

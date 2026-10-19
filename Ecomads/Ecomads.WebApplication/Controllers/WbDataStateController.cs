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
        if (startDate == default || endDate < startDate || endDate > WbSyncPlanner.Today() ||
            endDate.DayNumber - startDate.DayNumber > 365) return BadRequest();
        var stores = campaignId.HasValue
            ? await db.Campaigns.Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
                .Select(x => x.StoreId).ToArrayAsync(ct)
            : await db.Stores.Where(x => x.SellerId == sellerId && x.ApiKey != null)
                .Select(x => x.Id).ToArrayAsync(ct);
        if (campaignId.HasValue && stores.Length == 0) return NotFound();
        var jobs = await db.WbSyncJobs.AsNoTracking().Where(x => stores.Contains(x.StoreId) &&
            x.StartDate <= endDate && x.EndDate >= startDate)
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
            .Select(x => new { x.CampaignId, x.Date, x.CheckedAtUtc, x.Result, x.LastAttemptAtUtc, x.LastAttemptResult }).ToArrayAsync(ct);
        var missingChecks = checks.Where(x => x.LastAttemptResult == "no_data" &&
            x.LastAttemptAtUtc >= x.CheckedAtUtc).ToArray();
        var availableChecks = checks.Where(x => x.Result is "data" or "zero").ToArray();
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
        var quality = await db.WbSourceQualities.AsNoTracking().Where(x => stores.Contains(x.StoreId) &&
            x.StartDate <= endDate && x.EndDate >= startDate).ToArrayAsync(ct);
        if (campaignId.HasValue)
        {
            var key = campaignId.Value.ToString();
            var articleKeys = campaignArticleIds.Select(x => x.ToString()).ToHashSet();
            quality = quality.Where(x => x.Source == "fullstats" && (x.Entity == "" || x.Entity == key || x.Entity.StartsWith(key + ":", StringComparison.Ordinal)) ||
                x.Source == "clusters" && (x.Entity == "" || x.Entity.StartsWith(key + ":", StringComparison.Ordinal)) ||
                x.Source == "jam" && (x.Entity == "" || articleKeys.Contains(x.Entity))).ToArray();
        }

        object Source(string kind, string[] jobKinds, int covered, int expected, DateTime? verifiedAt,
            DateOnly? availableStartDate, DateOnly? availableEndDate)
        {
            var relevant = jobs.Where(x => jobKinds.Contains(x.Kind)).ToArray();
            var active = relevant.FirstOrDefault(x => x.Status is "pending" or "running");
            var latest = relevant.FirstOrDefault();
            var unresolvedFailure = relevant.FirstOrDefault(x => x.Status == "failed" &&
                !relevant.Any(next => next.Kind == x.Kind && next.Status == "completed" &&
                    next.UpdatedAtUtc > x.UpdatedAtUtc));
            var noDataCampaigns = kind == "fullstats" ? missingChecks.Select(x => x.CampaignId).Distinct().Count() : 0;
            var qualityRows = quality.Where(x => x.Source == kind).ToArray();
            var entities = qualityRows.Select(x => x.Entity).Distinct().ToArray();
            if (entities.Any(x => x.Length > 0)) entities = entities.Where(x => x.Length > 0).ToArray();
            var unresolvedQuality = qualityRows.Where(x => entities.Contains(x.Entity)).Where(row =>
                !WbQuality.Confirmed(qualityRows.Where(x => x.StoreId == row.StoreId &&
                        (x.Entity == row.Entity || x.Entity == "") && x.StartDate <= row.StartDate && x.EndDate >= row.EndDate)
                    .OrderByDescending(x => x.LastAttemptAtUtc).First().State)).ToArray();
            var status = active != null ? active.WaitReason is "rate_limit" or "rollout" ? "waiting" : "loading" :
                unresolvedFailure != null ? "failed" : covered == 0 ? "not_loaded" :
                    covered < expected ? "partial" : "complete";
            if (active == null && unresolvedFailure == null && (noDataCampaigns > 0 || unresolvedQuality.Length > 0)) status = "partial";
            var qualityVersion = qualityRows.Length == 0 ? 0 : qualityRows.Max(x => Math.Max(x.LastAttemptAtUtc.Ticks, x.AcceptedAtUtc?.Ticks ?? 0));
            var version = Math.Max(qualityVersion, Math.Max(verifiedAt?.Ticks ?? 0, latest?.UpdatedAtUtc.Ticks ?? 0)).ToString();
            return new { kind, status, startDate, endDate, availableStartDate, availableEndDate,
                covered, expected, noDataCampaigns, lastCheckedAtUtc = verifiedAt,
                waitReason = active?.WaitReason,
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
                Source("fullstats", ["fullstats", "fullstats_month", "fullstats_history", "archive"], spendDays, allDays,
                    checks.Length == 0 ? null : checks.Max(x => x.LastAttemptAtUtc ?? x.CheckedAtUtc),
                    availableChecks.Length == 0 ? null : availableChecks.Min(x => x.Date),
                    availableChecks.Length == 0 ? null : availableChecks.Max(x => x.Date)),
                Source("clusters", ["clusters"], clusterDates.Length > 0 ? 1 : 0, 1,
                    jobs.FirstOrDefault(x => x.Kind == "clusters" && x.Status == "completed")?.CompletedAtUtc,
                    clusterDates.Length == 0 ? null : clusterDates.Min(),
                    clusterDates.Length == 0 ? null : clusterDates.Max()),
                Source("jam", ["jam"], jamChecks.Length > 0 ? 1 : 0, 1,
                    jamChecks.Length == 0 ? null : jamChecks.Max(x => x.CheckedAtUtc),
                    jamChecks.Length == 0 ? null : jamChecks.Min(x => x.StartDate),
                    jamChecks.Length == 0 ? null : jamChecks.Max(x => x.EndDate)) }
            : new[] {
                Source("fullstats", ["fullstats", "fullstats_month", "fullstats_history", "archive"], spendDays, allDays,
                    checks.Length == 0 ? null : checks.Max(x => x.LastAttemptAtUtc ?? x.CheckedAtUtc),
                    availableChecks.Length == 0 ? null : availableChecks.Min(x => x.Date),
                    availableChecks.Length == 0 ? null : availableChecks.Max(x => x.Date)),
                Source("expenses", ["expenses", "expenses_month", "expenses_history"], expenseDays, allDays,
                    expenseChecks.Length == 0 ? null : expenseChecks.Max(x => x.LoadedAtUtc),
                    expenseChecks.Length == 0 ? null : expenseChecks.Min(x => x.Date),
                    expenseChecks.Length == 0 ? null : expenseChecks.Max(x => x.Date)),
                Source("orders", ["funnel_month", "funnel_recent", "funnel_backfill", "funnel_year"], orderDays, allDays,
                    orderChecks.Length == 0 ? null : orderChecks.Max(x => x.LoadedAtUtc),
                    orderChecks.Length == 0 ? null : orderChecks.Min(x => x.Date),
                    orderChecks.Length == 0 ? null : orderChecks.Max(x => x.Date)) };
        return Ok(new { sources, quality, reportingTimeZone = "МСК", isPreliminary = endDate >= WbSyncPlanner.Today(), active = jobs.Any(x => x.Status is "pending" or "running") });
    }
}

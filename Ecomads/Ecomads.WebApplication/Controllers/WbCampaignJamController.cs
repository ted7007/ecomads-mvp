using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/campaigns")]
public sealed class WbCampaignJamController(EcomadsDbContext db) : ControllerBase
{
    public sealed record JamRow(string NomenclatureId, string NomenclatureName, string SearchText,
        long? Frequency, long? WeekFrequency, decimal? AveragePosition, decimal? MedianPosition,
        long? OpenCard, long? AddToCart, long? Orders, bool MatchingLoadedAdCluster);
    public sealed record JamResponse(string JamStatus, DateTime? JamCheckedAtUtc,
        DateOnly StartDate, DateOnly EndDate, int ArticleCount, int ArticlesWithQueries,
        IReadOnlyList<JamRow> Rows, string ReportStatus, int CheckedArticleCount, int EmptyArticleCount,
        IReadOnlyList<JamReportOption> AvailableReports);
    public sealed record JamReportOption(DateOnly StartDate, DateOnly EndDate, DateTime LoadedAtUtc);

    [HttpGet("{campaignId:guid}/jam")]
    public async Task<IActionResult> Get(Guid campaignId, [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        if (startDate.HasValue != endDate.HasValue) return BadRequest(new { message = "Укажите обе даты периода." });
        var campaign = await db.Campaigns.AsNoTracking()
            .Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
            .Select(x => new { x.StoreId, x.Store.JamStatus, x.Store.JamCheckedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (campaign == null) return NotFound();

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var allArticleIds = await db.CampaignNomenclatureStatistics.AsNoTracking()
            .Where(x => x.CampaignId == campaignId).Select(x => x.NomenclatureId).Distinct().ToArrayAsync(cancellationToken);
        var checkPeriods = await db.WbJamArticleChecks.AsNoTracking()
            .Where(x => x.StoreId == campaign.StoreId && allArticleIds.Contains(x.NomenclatureId))
            .Select(x => new JamReportOption(x.StartDate, x.EndDate, x.CheckedAtUtc)).ToListAsync(cancellationToken);
        var legacyPeriods = await db.WbJamSearchQueries.AsNoTracking()
            .Where(x => x.StoreId == campaign.StoreId && allArticleIds.Contains(x.NomenclatureId))
            .Select(x => new JamReportOption(x.StartDate, x.EndDate, x.LoadedAtUtc)).ToListAsync(cancellationToken);
        var reports = checkPeriods.Concat(legacyPeriods)
            .GroupBy(x => (x.StartDate, x.EndDate))
            .Select(x => new JamReportOption(x.Key.StartDate, x.Key.EndDate, x.Max(y => y.LoadedAtUtc)))
            .OrderByDescending(x => x.EndDate).ThenByDescending(x => x.LoadedAtUtc).ToArray();
        var selected = !startDate.HasValue ? reports.FirstOrDefault() : null;
        var end = endDate ?? selected?.EndDate ?? yesterday;
        var start = startDate ?? selected?.StartDate ?? end.AddDays(-6);
        if (start > end) return BadRequest(new { message = "Некорректный период." });
        var fromUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var throughUtc = DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var articleIds = await db.CampaignNomenclatureStatistics.AsNoTracking()
            .Where(x => x.CampaignId == campaignId && x.Date >= fromUtc && x.Date <= throughUtc)
            .Select(x => x.NomenclatureId).Distinct().ToArrayAsync(cancellationToken);
        var raw = await (from query in db.WbJamSearchQueries.AsNoTracking()
            join article in db.Nomenclatures.AsNoTracking() on query.NomenclatureId equals article.Id
            where query.StoreId == campaign.StoreId && articleIds.Contains(query.NomenclatureId) &&
                query.StartDate == start && query.EndDate == end
            select new { query, article.WbNomenclatureId, article.Name })
            .ToListAsync(cancellationToken);
        var articleChecks = await db.WbJamArticleChecks.AsNoTracking().Where(x => x.StoreId == campaign.StoreId &&
                articleIds.Contains(x.NomenclatureId) && x.StartDate == start && x.EndDate == end)
            .ToArrayAsync(cancellationToken);
        var clusterNames = await db.WbClusterStatistics.AsNoTracking()
            .Where(x => x.CampaignId == campaignId && articleIds.Contains(x.NomenclatureId) &&
                x.Date >= start && x.Date <= end)
            .Select(x => new { x.NomenclatureId, x.ClusterName }).Distinct()
            .ToListAsync(cancellationToken);
        var loadedClusterKeys = clusterNames.Select(x => (x.NomenclatureId, x.ClusterName.Trim().ToLowerInvariant()))
            .ToHashSet();
        var rows = raw.Select(x => new JamRow(x.WbNomenclatureId, x.Name, x.query.SearchText,
                x.query.Frequency, x.query.WeekFrequency, x.query.AveragePosition, x.query.MedianPosition,
                x.query.OpenCard, x.query.AddToCart, x.query.Orders,
                loadedClusterKeys.Contains((x.query.NomenclatureId, x.query.SearchText.Trim().ToLowerInvariant()))))
            .OrderByDescending(x => x.Orders).ThenByDescending(x => x.Frequency).ToList();
        var reportStatus = campaign.JamStatus is "access_denied" or "payment_required" ? "access_denied" :
            articleIds.Length > 0 && articleChecks.Length == articleIds.Length ?
                raw.Count == 0 ? "empty" : "complete" :
            articleChecks.Length > 0 ? "partial" : raw.Count > 0 ? "legacy" : "not_loaded";
        return Ok(new JamResponse(campaign.JamStatus, campaign.JamCheckedAtUtc, start, end,
            articleIds.Length, raw.Select(x => x.query.NomenclatureId).Distinct().Count(), rows,
            reportStatus, articleChecks.Length, articleChecks.Count(x => !x.HasData), reports));
    }
}

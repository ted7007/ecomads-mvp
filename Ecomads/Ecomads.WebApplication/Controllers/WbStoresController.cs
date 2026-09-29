using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/stores")]
public sealed class WbStoresController(
    EcomadsDbContext db,
    IWbPromotionClient wb,
    IWbTokenService tokens) : ControllerBase
{
    public sealed record ConnectRequest(string Token);
    public sealed record SyncRequest(DateOnly? StartDate, DateOnly? EndDate, long[]? CampaignIds);
    public sealed record SyncResponse(Guid Id, string Kind, string Status, DateOnly StartDate, DateOnly EndDate,
        int ProcessedCampaigns, int TotalCampaigns, DateTime NextAttemptAtUtc, string? ErrorCode);
    public sealed record StoreResponse(
        Guid Id,
        string Name,
        string ExternalId,
        string TokenLastFour,
        DateTime TokenExpiresAtUtc,
        DateTime? LastSyncAt,
        int CampaignCount);
    public sealed record CampaignListItem(Guid Id, string Name, string WbCampaignId, int? WbStatus);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var stores = await db.Stores.AsNoTracking()
            .Where(x => x.SellerId == sellerId && x.ExternalId != null && x.ApiKey != null)
            .Select(x => new StoreResponse(
                x.Id,
                x.Name,
                x.ExternalId!,
                x.TokenLastFour ?? string.Empty,
                x.TokenExpiresAtUtc ?? DateTime.MinValue,
                x.LastSyncAt,
                db.Campaigns.Count(c => c.StoreId == x.Id)))
            .ToListAsync(cancellationToken);
        return Ok(stores);
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect([FromBody] ConnectRequest request, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();

        var rawToken = request.Token?.Trim() ?? string.Empty;
        WbTokenClaims claims;
        try
        {
            claims = tokens.ReadClaims(rawToken);
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { message = error.Message });
        }

        var existing = await db.Stores.SingleOrDefaultAsync(
            x => x.Marketplace == "Wildberries" && x.ExternalId == claims.SellerId,
            cancellationToken);
        if (existing != null && existing.SellerId != sellerId)
        {
            return Conflict(new { message = "Этот кабинет WB уже подключён к другому аккаунту EcomAds." });
        }

        IReadOnlyList<WbCampaignInfo> adverts;
        try
        {
            adverts = await wb.GetCampaignsAsync(rawToken, cancellationToken);
        }
        catch (WbApiException error)
        {
            if (error.RetryAfter.HasValue)
            {
                Response.Headers.RetryAfter = Math.Ceiling(error.RetryAfter.Value.TotalSeconds).ToString("F0");
            }
            // A WB 401 must not be forwarded as our application's 401: the client would clear its own session.
            var clientStatus = error.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? StatusCodes.Status422UnprocessableEntity : (int)error.StatusCode;
            return StatusCode(clientStatus, new { message = "Не удалось проверить токен через WB API.", wbStatus = (int)error.StatusCode });
        }

        var store = existing ?? await db.Stores.FirstOrDefaultAsync(
            x => x.SellerId == sellerId && x.ExternalId == null && x.ApiKey == null,
            cancellationToken) ?? new Store { Id = Guid.NewGuid(), SellerId = sellerId, CreatedAt = DateTime.UtcNow };

        if (db.Entry(store).State == EntityState.Detached) db.Stores.Add(store);
        store.Marketplace = "Wildberries";
        store.ExternalId = claims.SellerId;
        store.ApiKey = tokens.Protect(rawToken);
        store.TokenLastFour = rawToken[^4..];
        store.TokenExpiresAtUtc = claims.ExpiresAtUtc;
        if (existing == null) store.LastSyncAt = null;
        if (string.IsNullOrWhiteSpace(store.Name) || store.Name.EndsWith(" store", StringComparison.Ordinal))
        {
            store.Name = "Кабинет WB";
        }

        var existingCampaigns = await db.Campaigns.Where(x => x.StoreId == store.Id).ToListAsync(cancellationToken);
        var campaignById = existingCampaigns.ToDictionary(x => x.WbCampaignId, StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        foreach (var advert in adverts)
        {
            var wbId = advert.Id.ToString();
            if (!campaignById.TryGetValue(wbId, out var campaign))
            {
                campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = wbId, CreatedAt = now };
                db.Campaigns.Add(campaign);
            }
            campaign.Name = advert.Name;
            campaign.IsActive = advert.Status == 9;
            campaign.WbStatus = advert.Status;
            campaign.LastSeenAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new StoreResponse(store.Id, store.Name, store.ExternalId, store.TokenLastFour,
            store.TokenExpiresAtUtc.Value, store.LastSyncAt, adverts.Count));
    }

    [HttpGet("{storeId:guid}/campaigns")]
    public async Task<IActionResult> ListCampaigns(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken)) return NotFound();
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId)
            .OrderByDescending(x => x.WbStatus == 9).ThenBy(x => x.Name)
            .Select(x => new CampaignListItem(x.Id, x.Name, x.WbCampaignId, x.WbStatus))
            .ToListAsync(cancellationToken);
        return Ok(campaigns);
    }

    [HttpDelete("{storeId:guid}")]
    public async Task<IActionResult> Disconnect(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken);
        if (store == null) return NotFound();
        store.ApiKey = null;
        store.TokenLastFour = null;
        store.TokenExpiresAtUtc = null;
        var activeJobs = await db.WbSyncJobs.Where(x => x.StoreId == storeId &&
            (x.Status == "pending" || x.Status == "running")).ToListAsync(cancellationToken);
        foreach (var job in activeJobs)
        {
            job.Status = "failed";
            job.ErrorCode = "token_disconnected";
            job.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{storeId:guid}/sync")]
    public async Task<IActionResult> GetSync(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken))
            return NotFound();
        var job = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == storeId)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return job == null ? NoContent() : Ok(ToSyncResponse(job));
    }

    [HttpPost("{storeId:guid}/sync")]
    public async Task<IActionResult> StartSync(Guid storeId, [FromBody] SyncRequest? request, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken);
        if (store?.ApiKey == null) return NotFound(new { message = "Подключённый кабинет WB не найден." });

        var active = await db.WbSyncJobs.FirstOrDefaultAsync(x => x.StoreId == storeId &&
            (x.Status == "pending" || x.Status == "running"), cancellationToken);
        if (active != null) return active.Kind == "fullstats"
            ? Ok(ToSyncResponse(active))
            : Conflict(new { message = "Сначала дождитесь завершения сбора кластеров." });

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var end = request?.EndDate ?? yesterday;
        var start = request?.StartDate ?? end.AddDays(-29);
        if (start > end || end > yesterday || end.DayNumber - start.DayNumber > 30)
        {
            return BadRequest(new { message = "Выберите завершённый период не более 31 дня." });
        }

        var campaignIds = await db.Campaigns.Where(x => x.StoreId == storeId &&
            (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11))
            .OrderBy(x => x.WbStatus == 9 ? 0 : x.WbStatus == 11 ? 1 : 2)
            .Select(x => new { x.WbCampaignId, x.WbStatus }).ToListAsync(cancellationToken);
        var availableIds = campaignIds
            .Select(x => long.TryParse(x.WbCampaignId, out var id) ? id : 0)
            .Where(x => x > 0).ToHashSet();
        var requestedIds = request?.CampaignIds?.Distinct().ToArray();
        if (requestedIds is { Length: > 0 } && requestedIds.Any(x => !availableIds.Contains(x)))
        {
            return BadRequest(new { message = "Выбрана кампания, недоступная для статистики этого кабинета." });
        }
        var ids = requestedIds is { Length: > 0 }
            ? requestedIds
            : campaignIds.Where(x => x.WbStatus is 9 or 11)
                .Select(x => long.TryParse(x.WbCampaignId, out var id) ? id : 0)
                .Where(x => x > 0).ToArray();
        if (ids.Length == 0) return BadRequest(new { message = "Нет кампаний WB, для которых доступна статистика." });

        var lastRequest = await db.WbSyncJobs.Where(x => x.StoreId == storeId && x.LastRequestAtUtc != null)
            .MaxAsync(x => x.LastRequestAtUtc, cancellationToken);
        var now = DateTime.UtcNow;
        var job = new WbSyncJob
        {
            Id = Guid.NewGuid(), StoreId = storeId, StartDate = start, EndDate = end,
            CampaignIdsJson = JsonSerializer.Serialize(ids), Kind = "fullstats", Status = "pending",
            CreatedAtUtc = now, UpdatedAtUtc = now,
            NextAttemptAtUtc = lastRequest.HasValue && lastRequest.Value.AddHours(1) > now
                ? lastRequest.Value.AddHours(1) : now
        };
        db.WbSyncJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(ToSyncResponse(job));
    }

    [HttpPost("{storeId:guid}/clusters/sync")]
    public async Task<IActionResult> StartClusterSync(Guid storeId, [FromBody] SyncRequest? request, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken);
        if (store?.ApiKey == null) return NotFound(new { message = "Подключённый кабинет WB не найден." });
        var active = await db.WbSyncJobs.FirstOrDefaultAsync(x => x.StoreId == storeId &&
            (x.Status == "pending" || x.Status == "running"), cancellationToken);
        if (active != null) return active.Kind == "clusters"
            ? Ok(ToSyncResponse(active))
            : Conflict(new { message = "Сначала дождитесь завершения сбора статистики кампаний." });

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var end = request?.EndDate ?? yesterday;
        var start = request?.StartDate ?? end.AddDays(-6);
        if (start > end || end > yesterday || end.DayNumber - start.DayNumber > 6)
            return BadRequest(new { message = "Для кластеров выберите завершённый период не более 7 дней." });

        var selectedIds = request?.CampaignIds?.Distinct().ToArray();
        if (selectedIds is { Length: > 0 })
        {
            var ownedIds = await db.Campaigns.Where(x => x.StoreId == storeId)
                .Select(x => x.WbCampaignId).ToListAsync(cancellationToken);
            var owned = ownedIds.Where(x => long.TryParse(x, out _)).Select(long.Parse).ToHashSet();
            if (selectedIds.Any(x => !owned.Contains(x)))
                return BadRequest(new { message = "Выбрана кампания другого кабинета." });
        }

        var startUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var coveringJobs = await db.WbSyncJobs.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Kind == "fullstats" && x.Status == "completed" &&
                x.StartDate <= start && x.EndDate >= end)
            .Select(x => x.CampaignIdsJson).ToListAsync(cancellationToken);
        var loadedIds = coveringJobs.SelectMany(x => JsonSerializer.Deserialize<long[]>(x) ?? [])
            .ToHashSet();
        var observed = await (from stat in db.CampaignNomenclatureStatistics
            join campaign in db.Campaigns on stat.CampaignId equals campaign.Id
            join article in db.Nomenclatures on stat.NomenclatureId equals article.Id
            where campaign.StoreId == storeId && stat.Date >= startUtc && stat.Date <= endUtc
            select new { campaign.WbCampaignId, article.WbNomenclatureId })
            .Distinct().ToListAsync(cancellationToken);
        var pairs = observed.Where(x => long.TryParse(x.WbCampaignId, out _) &&
                long.TryParse(x.WbNomenclatureId, out _))
            .Select(x => new WbNormQueryPair(long.Parse(x.WbCampaignId), long.Parse(x.WbNomenclatureId)))
            .Where(x => loadedIds.Contains(x.AdvertId) &&
                (selectedIds is not { Length: > 0 } || selectedIds.Contains(x.AdvertId)))
            .ToArray();
        if (pairs.Length == 0)
            return BadRequest(new { message = "Сначала загрузите статистику выбранных кампаний за этот период." });

        var lastRequest = await db.WbSyncJobs.Where(x => x.StoreId == storeId && x.LastRequestAtUtc != null)
            .MaxAsync(x => x.LastRequestAtUtc, cancellationToken);
        var now = DateTime.UtcNow;
        var job = new WbSyncJob
        {
            Id = Guid.NewGuid(), StoreId = storeId, StartDate = start, EndDate = end,
            CampaignIdsJson = "[]", PairIdsJson = JsonSerializer.Serialize(pairs), Kind = "clusters", Status = "pending",
            CreatedAtUtc = now, UpdatedAtUtc = now,
            NextAttemptAtUtc = lastRequest.HasValue && lastRequest.Value.AddHours(1) > now
                ? lastRequest.Value.AddHours(1) : now
        };
        db.WbSyncJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(ToSyncResponse(job));
    }

    private static SyncResponse ToSyncResponse(WbSyncJob job)
    {
        var total = job.Kind == "clusters"
            ? (JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson ?? "[]") ?? []).Length
            : (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Length;
        return new SyncResponse(job.Id, job.Kind, job.Status, job.StartDate, job.EndDate,
            job.NextCampaignOffset, total, job.NextAttemptAtUtc, job.ErrorCode);
    }

    private bool TrySellerId(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);
}

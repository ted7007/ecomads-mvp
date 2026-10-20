using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/stores")]
public sealed class WbStoresController(
    EcomadsDbContext db,
    IWbPromotionClient wb,
    IWbTokenService tokens,
    WbSyncPlanner planner,
    IConfiguration configuration, TimeProvider? clock = null) : ControllerBase
{
    public sealed record AutoRefreshRequest(bool Enabled);
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
        int CampaignCount,
        string JamStatus,
        DateTime? JamCheckedAtUtc,
        bool AutoRefreshEnabled, bool AutoRefreshAvailable = false, bool AutoRefreshBlocked = false);
    public sealed record CampaignListItem(Guid Id, string Name, string WbCampaignId, int? WbStatus);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var autoRefreshEnabled = configuration.GetValue<bool>("Wb:AutoRefresh:Enabled");
        var stores = await db.Stores.AsNoTracking()
            .Where(x => x.SellerId == sellerId && x.ExternalId != null && x.ApiKey != null)
            .Select(x => new StoreResponse(
                x.Id,
                x.Name,
                x.ExternalId!,
                x.TokenLastFour ?? string.Empty,
                x.TokenExpiresAtUtc ?? DateTime.MinValue,
                x.LastSyncAt,
                db.Campaigns.Count(c => c.StoreId == x.Id),
                x.JamStatus, x.JamCheckedAtUtc, x.AutoRefreshEnabled, autoRefreshEnabled, x.AutoRefreshBlocked))
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

        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var store = existing ?? await db.Stores.FirstOrDefaultAsync(
            x => x.SellerId == sellerId && x.ExternalId == null && x.ApiKey == null,
            cancellationToken) ?? new Store { Id = Guid.NewGuid(), SellerId = sellerId, CreatedAt = now, Name = "Кабинет WB" };
        // Persist only an unconnected placeholder so an unsuccessful first response can
        // still be retained under the correct owner. Credentials are set after validation.
        if (db.Entry(store).State == EntityState.Detached)
        { db.Stores.Add(store); await db.SaveChangesAsync(cancellationToken); }
        using var capture = new WbIngestionContext(new() { StoreId = store.Id, Kind = "campaign_list",
            StartDate = WbSyncPlanner.Today(now), EndDate = WbSyncPlanner.Today(now) }, now);
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
        catch (JsonException)
        {
            if (capture.SnapshotId is { } id)
            {
                var snapshot = await db.WbResponseSnapshots.FindAsync([id], cancellationToken);
                if (snapshot != null) { snapshot.State = "error"; snapshot.Reason = "invalid_response"; await db.SaveChangesAsync(cancellationToken); }
            }
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "WB вернул некорректный список кампаний. Ответ сохранён для проверки интеграции." });
        }

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            store = await db.Stores.SingleAsync(x => x.Id == store.Id, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"wb-ingest:" + store.Id}, 0))", cancellationToken);
            store.Marketplace = "Wildberries";
            store.ExternalId = claims.SellerId;
            store.ApiKey = tokens.Protect(rawToken);
            store.TokenLastFour = rawToken[^4..];
            store.TokenExpiresAtUtc = claims.ExpiresAtUtc;
            store.AutoRefreshBlocked = false;
            store.JamStatus = "unknown";
            store.JamCheckedAtUtc = null;
            if (existing == null) store.LastSyncAt = null;
            if (string.IsNullOrWhiteSpace(store.Name) || store.Name.EndsWith(" store", StringComparison.Ordinal))
            {
                store.Name = "Кабинет WB";
            }

            await planner.UpsertCampaignsAsync(store, adverts, cancellationToken);
            if (capture.SnapshotId is { } snapshotId)
            {
                var snapshot = await db.WbResponseSnapshots.SingleAsync(x => x.Id == snapshotId, cancellationToken);
                snapshot.State = capture.HasIssues ? "partial" : "applied";
                snapshot.AppliedAtUtc = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            }
            await WbRateLimits.DeferUntilAsync(db, store.Id, "campaign_list", 0, DateTime.UtcNow.AddHours(1), cancellationToken);
            await db.WbRefreshSchedules.Where(x => x.StoreId == store.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PlannedSlotUtc, (DateTime?)null), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        return Ok(new StoreResponse(store.Id, store.Name, claims.SellerId, rawToken[^4..],
            claims.ExpiresAtUtc, store.LastSyncAt, adverts.Count,
            store.JamStatus, store.JamCheckedAtUtc, store.AutoRefreshEnabled,
            configuration.GetValue<bool>("Wb:AutoRefresh:Enabled"), store.AutoRefreshBlocked));
    }

    [HttpPut("{storeId:guid}/auto-refresh")]
    public async Task<IActionResult> SetAutoRefresh(Guid storeId, [FromBody] AutoRefreshRequest request, CancellationToken ct)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId && x.ApiKey != null, ct);
        if (store == null) return NotFound();
        store.AutoRefreshEnabled = request.Enabled;
        await db.SaveChangesAsync(ct);
        return Ok(new { enabled = store.AutoRefreshEnabled });
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

    [HttpPost("{storeId:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken);
        if (store?.ApiKey == null) return NotFound(new { message = "Подключённый кабинет WB не найден." });
        await WbProductOrdersRefresh.RequestManualAsync(db, store.Id, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, cancellationToken);
        var run = await planner.StartRefreshAsync(store, cancellationToken);
        var response = new { runId = run.RunId, campaignsRefreshed = run.CampaignsRefreshed,
            jobs = run.Jobs.Select(WbSyncVisibilityController.View), skipped = run.Skipped };
        return StatusCode(run.AlreadyActive ? StatusCodes.Status200OK : StatusCodes.Status202Accepted, response);
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
        store.JamStatus = "unknown";
        store.JamCheckedAtUtc = null;
        var activeJobs = await db.WbSyncJobs.Where(x => x.StoreId == storeId &&
            (x.Status == "pending" || x.Status == "running")).ToListAsync(cancellationToken);
        foreach (var job in activeJobs)
        {
            job.Status = "failed";
            job.Stage = "failed";
            job.ErrorCode = "token_disconnected";
            job.UpdatedAtUtc = DateTime.UtcNow;
            job.CompletedAtUtc = job.UpdatedAtUtc;
            db.WbSyncJobEvents.Add(new WbSyncJobEvent { JobId = job.Id, OccurredAtUtc = job.UpdatedAtUtc,
                Stage = job.Stage, ErrorCode = job.ErrorCode, ProcessedCount = job.NextCampaignOffset });
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

        var active = await db.WbSyncJobs.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Kind == "fullstats" &&
            (x.Status == "pending" || x.Status == "running"), cancellationToken);
        if (active != null) return Ok(ToSyncResponse(active));

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var end = request?.EndDate ?? yesterday;
        var start = request?.StartDate ?? end.AddDays(-30);
        if (start > end || end > yesterday || end.DayNumber - start.DayNumber > 30)
        {
            return BadRequest(new { message = "Выберите завершённый период не более 31 дня." });
        }

        var result = await planner.EnqueueFullStatsAsync(store, start, end, request?.CampaignIds, cancellationToken);
        return result.Job == null ? BadRequest(new { message = result.Reason }) : Accepted(ToSyncResponse(result.Job));
    }

    [HttpPost("{storeId:guid}/history/year")]
    public async Task<IActionResult> StartYearHistory(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId,
            cancellationToken);
        if (store?.ApiKey == null) return NotFound(new { message = "Подключённый кабинет WB не найден." });
        var end = WbSyncPlanner.Yesterday();
        var start = end.AddDays(-364);
        var fullstats = await planner.EnqueueYearFullStatsAsync(store, start, end, cancellationToken);
        var expenses = await planner.EnqueueYearExpensesAsync(store, start, end, cancellationToken);
        var orders = await planner.EnqueueYearOrdersAsync(store, start, end, cancellationToken);
        return Accepted(new { startDate = start, endDate = end,
            jobs = new[] { fullstats.Job, expenses.Job, orders.Job }.Where(x => x != null).Select(x =>
                new { x!.Id, x.Kind, x.Status, x.StartDate, x.EndDate }),
            skipped = new[] { new { kind = "fullstats_history", fullstats.Reason },
                new { kind = "expenses_history", expenses.Reason },
                new { kind = "funnel_year", orders.Reason } }.Where(x => x.Reason != null) });
    }

    [HttpPost("{storeId:guid}/clusters/sync")]
    public async Task<IActionResult> StartClusterSync(Guid storeId, [FromBody] SyncRequest? request, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken);
        if (store?.ApiKey == null) return NotFound(new { message = "Подключённый кабинет WB не найден." });
        var active = await db.WbSyncJobs.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Kind == "clusters" &&
            (x.Status == "pending" || x.Status == "running"), cancellationToken);
        if (active != null) return Ok(ToSyncResponse(active));

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var end = request?.EndDate ?? yesterday;
        var start = request?.StartDate ?? end.AddDays(-6);
        if (start > end || end > yesterday || end.DayNumber - start.DayNumber > 6)
            return BadRequest(new { message = "Для кластеров выберите завершённый период не более 7 дней." });

        var result = await planner.EnqueueClustersAsync(store, start, end, request?.CampaignIds, cancellationToken);
        return result.Job == null ? BadRequest(new { message = result.Reason }) : Accepted(ToSyncResponse(result.Job));
    }

    [HttpPost("{storeId:guid}/jam/sync")]
    public async Task<IActionResult> StartJamSync(Guid storeId, [FromBody] SyncRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken);
        if (store?.ApiKey == null) return NotFound(new { message = "Подключённый кабинет WB не найден." });
        var active = await db.WbSyncJobs.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Kind == "jam" &&
            (x.Status == "pending" || x.Status == "running"), cancellationToken);
        if (active != null) return Ok(ToSyncResponse(active));

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var end = request?.EndDate ?? yesterday;
        var start = request?.StartDate ?? end.AddDays(-6);
        if (start > end || end > yesterday || end.DayNumber - start.DayNumber > 6)
            return BadRequest(new { message = "Для отчёта Джема выберите завершённый период не более 7 дней." });

        var result = await planner.EnqueueJamAsync(store, start, end, request?.CampaignIds, cancellationToken);
        return result.Job == null ? BadRequest(new { message = result.Reason }) : Accepted(ToSyncResponse(result.Job));
    }

    private static SyncResponse ToSyncResponse(WbSyncJob job)
    {
        var total = WbSyncJobUnits.Total(job);
        return new SyncResponse(job.Id, job.Kind, job.Status, job.StartDate, job.EndDate,
            job.NextCampaignOffset, total, job.NextAttemptAtUtc, job.ErrorCode);
    }

    private bool TrySellerId(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);
}

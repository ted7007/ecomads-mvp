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
[Route("api/wb/stores/{storeId:guid}")]
public sealed class WbSyncVisibilityController(EcomadsDbContext db) : ControllerBase
{
    private static readonly string[] Kinds = ["fullstats", "funnel_recent", "expenses", "fullstats_history", "expenses_history", "funnel_year", "clusters", "jam", "archive", "funnel_backfill", "funnel"];
    private static readonly string[] Statuses = ["pending", "running", "completed", "failed"];

    [HttpGet("sync-overview")]
    public async Task<IActionResult> Overview(Guid storeId, CancellationToken cancellationToken)
    {
        if (!await Owns(storeId, cancellationToken)) return NotFound();
        var activeJobs = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == storeId &&
            (x.Status == "pending" || x.Status == "running"))
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var active = activeJobs.FirstOrDefault();
        var sources = new List<object>();
        foreach (var kind in Kinds)
        {
            var last = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == storeId && x.Kind == kind)
                .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
            if (kind == "funnel_backfill" && last == null)
            {
                var end = WbSyncPlanner.Yesterday();
                var start = end.AddDays(-29);
                var olderEnd = end.AddDays(-7);
                var loadedDays = await db.WbStoreDailyOrders.AsNoTracking().CountAsync(x =>
                    x.StoreId == storeId && x.Date >= start && x.Date <= olderEnd, cancellationToken);
                if (loadedDays == olderEnd.DayNumber - start.DayNumber + 1) continue;
            }
            var success = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == storeId &&
                x.Kind == kind && x.Status == "completed")
                .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefaultAsync(cancellationToken);
            var kindActive = activeJobs.FirstOrDefault(x => x.Kind == kind);
            sources.Add(new { kind, lastJob = last == null ? null : View(last),
                activeJob = kindActive == null ? null : View(kindActive),
                blockedReason = kindActive == null ? null : BlockedReason(kindActive),
                lastSuccessAtUtc = success?.CompletedAtUtc ?? success?.UpdatedAtUtc });
        }
        return Ok(new { sources, activeJob = active == null ? null : View(active),
            blockedReason = active == null ? null : BlockedReason(active) });
    }

    [HttpGet("sync-jobs")]
    public async Task<IActionResult> History(Guid storeId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, [FromQuery] string? kind = null,
        [FromQuery] string? status = null, CancellationToken cancellationToken = default)
    {
        if (!await Owns(storeId, cancellationToken)) return NotFound();
        if (page < 1 || pageSize is < 1 or > 100 || (kind != null && !Kinds.Contains(kind)) ||
            (status != null && !Statuses.Contains(status))) return BadRequest();
        var query = db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == storeId);
        if (kind != null) query = query.Where(x => x.Kind == kind);
        if (status != null) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var jobs = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Ok(new { items = jobs.Select(View), total, page, pageSize });
    }

    [HttpGet("sync-jobs/{jobId:guid}")]
    public async Task<IActionResult> Details(Guid storeId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await Owns(storeId, cancellationToken)) return NotFound();
        var job = await db.WbSyncJobs.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.Id == jobId,
            cancellationToken);
        if (job == null) return NotFound();
        var events = await db.WbSyncJobEvents.AsNoTracking().Where(x => x.JobId == jobId)
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.OccurredAtUtc, x.Stage, x.ErrorCode, x.ProcessedCount, x.AttemptNumber })
            .ToListAsync(cancellationToken);
        return Ok(new { job = View(job), campaignIds = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [],
            events });
    }

    [HttpPost("sync-jobs/{jobId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid storeId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await Owns(storeId, cancellationToken)) return NotFound();
        var failed = await db.WbSyncJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId && x.StoreId == storeId,
            cancellationToken);
        if (failed == null) return NotFound();
        if (failed.Status != "failed") return Conflict(new { message = "Повтор доступен только для завершившегося ошибкой задания." });
        if (failed.ErrorCode is "token_missing" or "token_disconnected" or "token_unreadable" or "wb_401" or "wb_403" or "wb_402")
            return Conflict(new { message = "Сначала исправьте подключение или доступ к данным WB." });
        if (await db.WbSyncJobs.AnyAsync(x => x.StoreId == storeId && x.Kind == failed.Kind &&
            (x.Status == "pending" || x.Status == "running"), cancellationToken))
            return Conflict(new { message = "Дождитесь завершения текущей загрузки." });
        var now = DateTime.UtcNow;
        var retry = new WbSyncJob
        {
            Id = Guid.NewGuid(), StoreId = storeId, Kind = failed.Kind,
            StartDate = failed.StartDate, EndDate = failed.EndDate,
            CampaignIdsJson = failed.CampaignIdsJson, PairIdsJson = failed.PairIdsJson,
            NextCampaignOffset = failed.NextCampaignOffset, RetriedFromJobId = failed.Id,
            Status = "pending", Stage = "queued", CreatedAtUtc = now, UpdatedAtUtc = now,
            NextAttemptAtUtc = await WbRateLimits.NextSlotAsync(db, storeId, failed.Kind, now, cancellationToken)
        };
        if (retry.NextAttemptAtUtc > now) { retry.Stage = "waiting"; retry.WaitReason = "rate_limit"; }
        db.WbSyncJobs.Add(retry);
        db.WbSyncJobEvents.Add(new WbSyncJobEvent { JobId = retry.Id, OccurredAtUtc = now,
            Stage = retry.Stage, ProcessedCount = retry.NextCampaignOffset });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" }) { return Conflict(new { message = "Другая загрузка уже запущена. Обновите статус." }); }
        return Accepted(View(retry));
    }

    [HttpPost("sync-jobs/{jobId:guid}/pause")]
    public async Task<IActionResult> Pause(Guid storeId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await Owns(storeId, cancellationToken)) return NotFound();
        var job = await db.WbSyncJobs.SingleOrDefaultAsync(x => x.StoreId == storeId && x.Id == jobId,
            cancellationToken);
        if (job == null) return NotFound();
        if (!WbSyncJobUnits.IsYearHistory(job.Kind)) return BadRequest(new { message = "Пауза доступна только для годовой истории." });
        if (job.Status is not ("pending" or "running"))
            return Conflict(new { message = "Это задание уже завершилось." });
        if (job.PauseRequestedAtUtc == null)
        {
            job.PauseRequestedAtUtc = DateTime.UtcNow;
            db.WbSyncJobEvents.Add(new WbSyncJobEvent { JobId = job.Id, OccurredAtUtc = job.PauseRequestedAtUtc.Value,
                Stage = "paused", ProcessedCount = job.NextCampaignOffset });
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok(View(job));
    }

    [HttpPost("sync-jobs/{jobId:guid}/resume")]
    public async Task<IActionResult> Resume(Guid storeId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await Owns(storeId, cancellationToken)) return NotFound();
        var job = await db.WbSyncJobs.SingleOrDefaultAsync(x => x.StoreId == storeId && x.Id == jobId,
            cancellationToken);
        if (job == null) return NotFound();
        if (!WbSyncJobUnits.IsYearHistory(job.Kind)) return BadRequest(new { message = "Продолжение доступно только для годовой истории." });
        if (job.Status is not ("pending" or "running"))
            return Conflict(new { message = "Это задание уже завершилось." });
        if (job.PauseRequestedAtUtc != null)
        {
            job.PauseRequestedAtUtc = null;
            db.WbSyncJobEvents.Add(new WbSyncJobEvent { JobId = job.Id, OccurredAtUtc = DateTime.UtcNow,
                Stage = "resumed", ProcessedCount = job.NextCampaignOffset });
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok(View(job));
    }

    private async Task<bool> Owns(Guid storeId, CancellationToken token) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId) &&
        await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == sellerId, token);

    private static string BlockedReason(WbSyncJob job)
    {
        var name = job.Kind switch { "funnel" or "funnel_recent" => "Свежие заказы", "funnel_backfill" or "funnel_year" => "История заказов",
            "fullstats_history" => "История рекламы за год", "expenses_history" => "Расходы за год",
            "archive" => "Архив рекламы",
            "clusters" => "Поисковые кластеры", "jam" => "Поисковые запросы Джема", _ => "Статистика кампаний" };
        if (job.PauseRequestedAtUtc != null) return $"{name} приостановлены. Продолжите загрузку, когда будет удобно.";
        if (job.WaitReason == "priority") return $"{name} ждут завершения загрузки текущих данных.";
        if (job.WaitReason is "rate_limit" or "retry")
        {
            var moscow = TimeZoneInfo.ConvertTimeFromUtc(job.NextAttemptAtUtc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
            if (job.NextAttemptAtUtc.AddMinutes(2) < DateTime.UtcNow)
                return $"{name}: запуск задерживается. Обновите статус; новое задание пока недоступно.";
            return $"{name} ожидают {(job.WaitReason == "rate_limit" ? "лимит WB" : "повторную попытку")}. Следующая попытка в {moscow:HH:mm} МСК.";
        }
        return $"{name} сейчас загружаются. Новое задание можно запустить после завершения.";
    }

    public static object View(WbSyncJob job)
    {
        var total = WbSyncJobUnits.Total(job);
        var remaining = Math.Max(0, total - job.NextCampaignOffset);
        var requests = (remaining + WbSyncJobUnits.BatchSize(job.Kind) - 1) / WbSyncJobUnits.BatchSize(job.Kind);
        DateTime? estimatedCompletionAtUtc = !WbSyncJobUnits.IsYearHistory(job.Kind) &&
            (job.Status is "pending" or "running") && requests > 0
            ? job.NextAttemptAtUtc.AddTicks(WbRateLimits.IntervalFor(job.Kind).Ticks * (requests - 1)) : null;
        var stage = job.Stage == "queued" && job.Status == "completed" ? "completed" :
            job.Stage == "queued" && job.Status == "failed" ? "failed" : job.Stage;
        var retryCount = job.Kind is "fullstats" or "archive"
            ? (JsonSerializer.Deserialize<long[]>(job.PairIdsJson ?? "[]") ?? []).Length : 0;
        return new { job.Id, job.Kind, role = job.Kind, rateMethod = WbRateLimits.MethodFor(job.Kind, job.NextCampaignOffset),
            job.ImportedRows, job.ItemsWithData, job.ItemsWithoutData,
            retryCount,
            warning = job.ItemsWithoutData <= 0 ? null : job.Kind == "fullstats_history"
                ? job.Status == "completed"
                    ? "WB не вернул статистику по части кампаний даже после повторного запроса."
                    : "WB пока не вернул статистику по части кампаний; повторный запрос добавлен в очередь."
                : "Часть запрошенных кампаний или товаров не вернула данных.",
            job.Status, stage, job.WaitReason, paused = job.PauseRequestedAtUtc != null &&
                (job.Status is "pending" or "running"), job.StartDate, job.EndDate,
            processedCount = job.NextCampaignOffset, totalCount = total,
            unit = WbSyncJobUnits.Unit(job.Kind), estimatedCompletionAtUtc,
            job.CreatedAtUtc, job.StartedAtUtc, job.UpdatedAtUtc, job.CompletedAtUtc,
            job.NextAttemptAtUtc, job.ErrorCode, job.RetriedFromJobId, job.RunId,
            canRetry = job.Status == "failed" && job.ErrorCode is not ("token_missing" or "token_disconnected" or "token_unreadable" or "wb_401" or "wb_403" or "wb_402") };
    }
}

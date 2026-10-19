using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController, Authorize, Route("api/wb/stores/{storeId:guid}/ingestion")]
public sealed class WbIngestionController(EcomadsDbContext db, WbResponseProcessor processor, TimeProvider clock) : ControllerBase
{
    private Guid SellerId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private Task<bool> Owns(Guid storeId, CancellationToken ct) => db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == SellerId, ct);

    [HttpGet("snapshots/{snapshotId:guid}")]
    public async Task<IActionResult> Snapshot(Guid storeId, Guid snapshotId, CancellationToken ct)
    {
        if (!await Owns(storeId, ct)) return NotFound();
        var snapshot = await db.WbResponseSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == snapshotId && x.StoreId == storeId, ct);
        return snapshot == null ? NotFound() : Ok(snapshot);
    }

    [HttpPost("snapshots/{snapshotId:guid}/replay")]
    public async Task<IActionResult> Replay(Guid storeId, Guid snapshotId, CancellationToken ct)
    {
        if (!await Owns(storeId, ct) || !await db.WbResponseSnapshots.AnyAsync(x => x.Id == snapshotId && x.StoreId == storeId, ct))
            return NotFound();
        try { await processor.ApplyAsync(snapshotId, true, ct); }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or FormatException or OverflowException)
        { return Conflict(new { reason = "Ответ сохранён, но не может быть применён текущим обработчиком." }); }
        return Ok(new { snapshotId });
    }

    public sealed record AcceptRecalculation(Guid SnapshotId);

    [HttpGet("expenses/{date}/evidence")]
    public async Task<IActionResult> Evidence(Guid storeId, DateOnly date, CancellationToken ct)
    {
        if (!await Owns(storeId, ct)) return NotFound();
        var rows = await db.WbCostObservations.AsNoTracking().Where(x => x.StoreId == storeId && x.MoscowDate == date)
            .OrderBy(x => x.SnapshotId).ThenBy(x => x.Ordinal).ToArrayAsync(ct);
        // Each snapshot is an alternative observation. Never sum across snapshots.
        return Ok(rows.GroupBy(x => x.SnapshotId).Select(g => new { snapshotId = g.Key, date,
            total = g.Where(x => x.State is "data" or "adjacent").Sum(x => x.Amount),
            partial = g.Any(x => x.State != "data"), operations = g.ToArray() }));
    }

    [HttpPost("expenses/{date}/accept-recalculation")]
    public async Task<IActionResult> Accept(Guid storeId, DateOnly date, AcceptRecalculation request, CancellationToken ct)
    {
        if (!await Owns(storeId, ct)) return NotFound();
        IActionResult result = Conflict();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"wb-ingest:" + storeId}, 0))", ct);
            var quality = await db.WbSourceQualities.FindAsync([storeId, "expenses", "", date, date], ct);
            if (quality?.SnapshotId != request.SnapshotId || quality.State != "revision" ||
                !quality.ProposedValue.HasValue || !quality.PreviousValue.HasValue)
            { result = Conflict(new { reason = "Пересчёт изменился либо содержит неоднозначность. Обновите данные." }); return; }
            var spend = await db.WbStoreDailySpends.SingleOrDefaultAsync(x => x.StoreId == storeId && x.Date == date, ct);
            if (spend == null || spend.Spend != quality.PreviousValue)
            { result = Conflict(new { reason = "Прежняя сумма изменилась. Обновите данные." }); return; }
            var now = clock.GetUtcNow().UtcDateTime;
            db.WbIngestionDecisions.Add(new WbIngestionDecision { Id = Guid.NewGuid(), StoreId = storeId,
                SellerId = SellerId, SnapshotId = request.SnapshotId, Date = date, PreviousValue = spend.Spend,
                AcceptedValue = quality.ProposedValue.Value, DecidedAtUtc = now });
            spend.Spend = quality.ProposedValue.Value;
            spend.LoadedAtUtc = quality.LastAttemptAtUtc;
            quality.State = "accepted";
            quality.Reason = "Пересчёт принят владельцем кабинета.";
            quality.AcceptedAtUtc = now; quality.AcceptedSnapshotId = request.SnapshotId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            result = Ok(new { date, spend = spend.Spend, state = quality.State });
        });
        return result;
    }
}

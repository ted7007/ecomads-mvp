using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbCostsImporter(EcomadsDbContext db, TimeProvider? clock = null)
{
    public async Task<int> ImportAsync(Guid storeId, DateOnly start, DateOnly end, JsonElement root,
        CancellationToken ct, bool allowAdjacentDays = false, bool fillMissingOnly = false)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("История затрат WB не является массивом.");
        var context = WbIngestionContext.Current;
        var now = context?.RequestedAtUtc ?? (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var quality = new WbQuality(db, clock);
        var parsed = WbCostSnapshot.Parse(root, start, end);
        var totals = parsed.Totals; var counts = parsed.Counts; var badDates = parsed.BadDates;
        var unknownDate = parsed.AmbiguousPeriod;
        var imported = parsed.Rows.Count(x => x.State == "data");
        foreach (var row in parsed.Rows)
        {
            if (context?.SnapshotId is not { } snapshotId) continue;
            var observation = await db.WbCostObservations.FindAsync([snapshotId, row.Ordinal], ct);
            if (observation == null)
            {
                observation = new() { SnapshotId = snapshotId, Ordinal = row.Ordinal, StoreId = storeId };
                db.WbCostObservations.Add(observation);
            }
            observation.RawJson = row.RawJson; observation.RawTime = row.RawTime;
            observation.OccurredAtUtc = row.OccurredAtUtc; observation.MoscowDate = row.MoscowDate;
            observation.Amount = row.Amount; observation.State = row.State;
        }
        var existing = await db.WbStoreDailySpends.Where(x => x.StoreId == storeId && x.Date >= start && x.Date <= end)
            .ToDictionaryAsync(x => x.Date, ct);
        foreach (var outside in totals.Keys.Where(x => x < start || x > end))
            await quality.ObserveAsync(storeId, "expenses_boundary", "", outside, outside, "partial", ct,
                "Операции сохранены; этот ответ не покрывает соседний день целиком.", proposed: totals[outside]);
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            existing.TryGetValue(date, out var old);
            if (fillMissingOnly && old != null) continue;
            var total = totals.GetValueOrDefault(date);
            // A neighbouring response may be the only time WB returned an operation.
            // Keep it as evidence; do not add overlapping snapshots together or call an
            // incomplete replacement a complete zero.
            var retainedBoundary = await db.WbSourceQualities.AsNoTracking().SingleOrDefaultAsync(x =>
                x.StoreId == storeId && x.Source == "expenses_boundary" && x.Entity == "" &&
                x.StartDate == date && x.EndDate == date, ct);
            var boundaryConflict = old == null && retainedBoundary?.ProposedValue > total;
            var state = unknownDate || badDates.Contains(date) || boundaryConflict ? "ambiguous" :
                old != null && total < old.Spend ? "revision" :
                !counts.ContainsKey(date) ? "no_operations" : total == 0 ? "zero" : "data";
            var reason = state switch {
                "ambiguous" => boundaryConflict ? "В соседнем ответе сохранены расходы, отсутствующие в новом снимке. Требуется сверка снимков." :
                    "В ответе есть операция с неоднозначной датой или суммой.",
                "revision" => !counts.ContainsKey(date) ? "В новой истории нет операций. Предлагаемый нулевой итог не подтверждает отсутствие расхода; прежняя сумма сохранена." :
                    "WB вернул меньший расход; требуется принять пересчёт.",
                "no_operations" => "В полученной истории нет операций. Это не явный ноль WB.", _ => null };
            if (!await quality.ObserveAsync(storeId, "expenses", "", date, date, state, ct, reason, old?.Spend, total)) continue;
            if (!WbQuality.Confirmed(state)) continue;
            if (old == null) { old = new() { StoreId = storeId, Date = date }; db.WbStoreDailySpends.Add(old); }
            old.Spend = total; old.LoadedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        return imported;
    }
}

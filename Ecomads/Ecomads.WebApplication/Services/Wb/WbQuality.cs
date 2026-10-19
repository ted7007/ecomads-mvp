using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbQuality(EcomadsDbContext db, TimeProvider? clock = null)
{
    public static bool Confirmed(string state) => state is "data" or "zero" or "accepted";
    public async Task<bool> ObserveAsync(Guid store, string source, string entity, DateOnly start, DateOnly end,
        string state, CancellationToken ct, string? reason = null, decimal? previous = null, decimal? proposed = null,
        DateTime? observedAtUtc = null)
    {
        var context = WbIngestionContext.Current;
        var at = observedAtUtc ?? context?.RequestedAtUtc ?? (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var row = await db.WbSourceQualities.FindAsync([store, source, entity, start, end], ct);
        if (row != null && row.LastAttemptAtUtc > at) return false;
        if (row == null)
        {
            row = new() { StoreId = store, Source = source, Entity = entity, StartDate = start, EndDate = end };
            db.WbSourceQualities.Add(row);
        }
        if (row.SnapshotId != null && row.SnapshotId == context?.SnapshotId && row.State == "accepted") return false;
        row.LastAttemptAtUtc = at; row.SnapshotId = context?.SnapshotId;
        row.State = state; row.Reason = reason; row.PreviousValue = previous; row.ProposedValue = proposed;
        if (Confirmed(state)) { row.AcceptedAtUtc = at; row.AcceptedSnapshotId = context?.SnapshotId; }
        else if (context != null) context.HasIssues = true;
        return true;
    }
}

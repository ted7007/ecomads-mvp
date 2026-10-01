using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public static class WbRateLimits
{
    public static TimeSpan IntervalFor(string kind) => kind switch
    {
        "clusters" or "funnel" => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(1)
    };

    public static async Task<DateTime> NextSlotAsync(EcomadsDbContext db, Guid storeId, string kind,
        DateTime now, CancellationToken cancellationToken)
    {
        var last = await db.WbSyncJobs.Where(x => x.StoreId == storeId && x.Kind == kind && x.LastRequestAtUtc != null)
            .MaxAsync(x => x.LastRequestAtUtc, cancellationToken);
        var next = last?.Add(IntervalFor(kind));
        return next > now ? next.Value : now;
    }
}

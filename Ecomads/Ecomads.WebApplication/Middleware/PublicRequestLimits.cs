using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ecomads.WebApplication.Middleware;

public static class PublicRequestLimits
{
    private static bool IsLead(HttpContext c) => c.Request.Method == "POST" &&
        c.Request.Path.Equals("/api/pilot-applications", StringComparison.OrdinalIgnoreCase);

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = 429;
        options.OnRejected = async (context, ct) =>
        {
            var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ?
                Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 10;
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Слишком много запросов. Попробуйте позже." }, ct);
        };
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(c => IsLead(c)
                ? RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => Window(5, TimeSpan.FromMinutes(10)))
                : RateLimitPartition.GetNoLimiter("other")),
            PartitionedRateLimiter.Create<HttpContext, string>(c => IsLead(c)
                ? RateLimitPartition.GetFixedWindowLimiter("leads", _ => Window(60, TimeSpan.FromMinutes(1)))
                : RateLimitPartition.GetNoLimiter("other")),
            PartitionedRateLimiter.Create<HttpContext, string>(c => IsLead(c)
                ? RateLimitPartition.GetConcurrencyLimiter("leads", _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter("other")));
        options.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(
            c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(10, TimeSpan.FromMinutes(1))));
        options.AddPolicy("register", c => RateLimitPartition.GetFixedWindowLimiter(
            c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(3, TimeSpan.FromHours(1))));
    }

    private static FixedWindowRateLimiterOptions Window(int limit, TimeSpan window) => new()
    { PermitLimit = limit, Window = window, QueueLimit = 0, AutoReplenishment = true };
}

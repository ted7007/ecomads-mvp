using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services;

public sealed class PilotApplicationOptions
{
    // Comma-separated immutable account IDs, never emails from public registration.
    public string AdminSellerIds { get; set; } = "";
}

public sealed class PilotApplicationAccess(IOptionsMonitor<PilotApplicationOptions> options)
{
    public bool CanManage(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
        options.CurrentValue.AdminSellerIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(value => Guid.TryParse(value, out var allowed) && allowed == id);
}

public sealed class PilotApplicationRetention(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<PilotApplicationRetention> logger) : BackgroundService
{
    public static Task<int> DeleteExpiredAsync(EcomadsDbContext db, DateTime now, CancellationToken ct) =>
        db.PilotApplications.Where(x => x.CreatedAtUtc <= now.AddDays(-180)).ExecuteDeleteAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await DeleteExpiredAsync(scope.ServiceProvider.GetRequiredService<EcomadsDbContext>(),
                    clock.GetUtcNow().UtcDateTime, stoppingToken);
                if (count > 0) logger.LogInformation("Expired pilot applications deleted: {Count}", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogError("Pilot application retention failed; will retry."); }
            await Task.Delay(TimeSpan.FromHours(24), clock, stoppingToken);
        }
    }
}

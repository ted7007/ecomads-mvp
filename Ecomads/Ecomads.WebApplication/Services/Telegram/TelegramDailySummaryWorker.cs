using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Telegram;

/// <summary>
/// Sends yesterday's summary automatically as soon as the data collection for that day is complete.
/// Uses the same sender and the same readiness check as the manual button, so a summary is never sent twice
/// and never sent from partly loaded data. Settings (all optional):
/// Telegram:AutoSummary:Enabled (default true), Telegram:AutoSummary:MinHourMsk (default 7),
/// Telegram:DashboardBaseUrl (default https://ecomads.ru).
/// </summary>
public sealed class TelegramDailySummaryWorker(IServiceScopeFactory scopes, ITelegramBotClient bot,
    IConfiguration configuration, ILogger<TelegramDailySummaryWorker> logger, TimeProvider? clock = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Telegram:AutoSummary:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (bot.IsConfigured) await RunOnceAsync((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Telegram automatic summary pass failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), clock ?? TimeProvider.System, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var nowMsk = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), moscow);
        // No night messages: wait until the configured morning hour (Moscow time).
        if (nowMsk.Hour < configuration.GetValue("Telegram:AutoSummary:MinHourMsk", 7)) return;
        var yesterday = DateOnly.FromDateTime(nowMsk).AddDays(-1);
        var baseUrl = (configuration["Telegram:DashboardBaseUrl"] ?? "https://ecomads.ru").TrimEnd('/');
        var dashboardUrl = $"{baseUrl}/dashboard?startDate={yesterday:yyyy-MM-dd}&endDate={yesterday:yyyy-MM-dd}";

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<TelegramDailySender>();
        // Same cabinets that are collected automatically; a blocked or expired cabinet has nothing reliable to send.
        var stores = await db.Stores.AsNoTracking().Where(x => x.ApiKey != null && x.AutoRefreshEnabled &&
            !x.AutoRefreshBlocked && (x.TokenExpiresAtUtc == null || x.TokenExpiresAtUtc > nowUtc)).ToListAsync(cancellationToken);
        foreach (var store in stores)
        {
            try
            {
                var chats = await db.TelegramChats.Where(x => x.SellerId == store.SellerId && x.IsActive)
                    .ToListAsync(cancellationToken);
                if (chats.Count == 0) continue;
                // Cheap pre-check, so the heavy report is only built while someone still has no summary for the day.
                var chatIds = chats.Select(x => x.Id).ToArray();
                var settled = await db.TelegramDeliveries.AsNoTracking().Where(x => x.StoreId == store.Id &&
                    x.ReportDate == yesterday && chatIds.Contains(x.ChatId) &&
                    (x.Status == "sent" || x.Status == "unknown")).Select(x => x.ChatId).ToListAsync(cancellationToken);
                if (chats.All(x => settled.Contains(x.Id))) continue;

                var result = await sender.SendAsync(store, chats, yesterday, dashboardUrl, true, cancellationToken);
                if (result.Sent > 0 || result.Failed > 0 || result.Unknown > 0)
                    logger.LogInformation("Telegram automatic summary for {Date}: sent {Sent}, failed {Failed}, unknown {Unknown}",
                        yesterday, result.Sent, result.Failed, result.Unknown);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Telegram automatic summary failed for store {StoreId}", store.Id);
            }
            db.ChangeTracker.Clear();
        }
    }
}

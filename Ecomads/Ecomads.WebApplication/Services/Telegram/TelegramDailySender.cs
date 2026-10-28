using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Telegram;

public sealed record TelegramDailyResult(DateOnly ReportDate, bool Collected, WbReportReadiness Readiness,
    int Sent, int Skipped, int Failed, int Unknown);

/// <summary>
/// Builds yesterday's summary and delivers it to the given chats with per-chat duplicate protection.
/// Shared by the manual button and the automatic worker so both follow exactly the same rules.
/// </summary>
public sealed class TelegramDailySender(EcomadsDbContext db, ITelegramBotClient bot, WbReportReadinessService reportReadiness)
{
    // One instance serves the app, so a process-wide lock is enough to keep two quick sends from racing.
    private static readonly SemaphoreSlim SendLock = new(1, 1);

    public async Task<TelegramDailyResult> SendAsync(Store store, IReadOnlyList<TelegramChat> chats, DateOnly yesterday,
        string dashboardUrl, bool automatic, CancellationToken ct)
    {
        var prepared = await ReadReportAsync(async () =>
        {
            var ready = await reportReadiness.GetAsync(store.Id, yesterday, ct);
            if (!ready.Collected) return (Readiness: ready, Message: (string?)null, Chart: (byte[]?)null);
            var day = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == store.Id &&
                ready.ConfirmedCampaignIds.Contains(x.Id))
                .Select(x => new { x.Id, x.WbCampaignId, x.Name }).ToListAsync(ct);

            var campaignIds = campaigns.Select(x => x.Id).ToArray();
            var stats = await db.CampaignStatistics.AsNoTracking()
                .Where(x => campaignIds.Contains(x.CampaignId) && x.Date == day)
                .Select(x => new { x.CampaignId, x.Spend, x.Revenue, x.Orders, x.Clicks, x.Impressions })
                .ToListAsync(ct);

            var campaignTargets = await db.WbCampaignNorms.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId))
                .Select(x => new { x.CampaignId, x.TargetDrr, x.CustomName }).ToDictionaryAsync(x => x.CampaignId, ct);
            var rows = campaigns.Select(c =>
            {
                var stat = stats.FirstOrDefault(s => s.CampaignId == c.Id);
                campaignTargets.TryGetValue(c.Id, out var custom);
                var spend = stat == null ? 0m : (decimal)stat.Spend;
                var drr = stat != null && stat.Revenue > 0 ? (decimal?)(spend * 100m / (decimal)stat.Revenue) : null;
                return new TelegramSummaryCampaign(custom?.CustomName ?? c.Name, spend, stat?.Orders ?? 0, drr);
            }).OrderByDescending(x => x.Spend).ToList();
            var totalSpend = rows.Sum(x => (decimal)x.Spend);
            var totalRevenue = stats.Sum(x => (decimal)x.Revenue);
            decimal? storeSpend = ready.Sources.Any(x => x.Kind == "expenses" && x.Ready) ?
                await db.WbStoreDailySpends.Where(x => x.StoreId == store.Id && x.Date == yesterday)
                    .Select(x => (decimal?)x.Spend).SingleOrDefaultAsync(ct) : null;
            decimal? storeOrders = ready.Sources.Any(x => x.Kind == "orders" && x.Ready) ?
                await db.WbStoreDailyOrders.Where(x => x.StoreId == store.Id && x.Date == yesterday)
                    .Select(x => (decimal?)x.OrderSum).SingleOrDefaultAsync(ct) : null;
            // Comparison with the previous day only where both days are fully confirmed; otherwise it is left out.
            var previousDate = yesterday.AddDays(-1);
            var previousReady = await reportReadiness.GetAsync(store.Id, previousDate, ct);
            decimal? previousStoreSpend = null, previousStoreOrders = null, previousAdSpend = null, previousAdRevenue = null;
            if (previousReady.Collected)
            {
                if (SourceReady(ready, "expenses") && SourceReady(previousReady, "expenses"))
                    previousStoreSpend = await db.WbStoreDailySpends.Where(x => x.StoreId == store.Id && x.Date == previousDate)
                        .Select(x => (decimal?)x.Spend).SingleOrDefaultAsync(ct);
                if (SourceReady(ready, "orders") && SourceReady(previousReady, "orders"))
                    previousStoreOrders = await db.WbStoreDailyOrders.Where(x => x.StoreId == store.Id && x.Date == previousDate)
                        .Select(x => (decimal?)x.OrderSum).SingleOrDefaultAsync(ct);
                if (ready.Ready && previousReady.Ready)
                {
                    var previousDay = DateTime.SpecifyKind(previousDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                    var previousStats = await db.CampaignStatistics.AsNoTracking()
                        .Where(x => campaignIds.Contains(x.CampaignId) && x.Date == previousDay)
                        .Select(x => new { x.Spend, x.Revenue }).ToListAsync(ct);
                    previousAdSpend = previousStats.Sum(x => (decimal)x.Spend);
                    previousAdRevenue = previousStats.Sum(x => (decimal)x.Revenue);
                }
            }
            var checkedAt = ready.Sources.Where(x => x.CheckedAtUtc.HasValue).Min(x => x.CheckedAtUtc);
            var text = TelegramSummaryFormatter.FormatHtml(new TelegramSummary(store.Name, yesterday,
                totalSpend, totalRevenue, stats.Sum(x => (long)x.Orders),
                rows.Select(x => new TelegramSummaryCampaign(x.Name, x.Spend, x.Orders, x.Drr)).ToArray(),
                MoscowText(checkedAt), dashboardUrl,
                MissingCampaignNames: ready.MissingCampaigns.Select(x => $"{x.Name} (ID {x.WbCampaignId})").ToArray(),
                SourceWarnings: ready.Sources.Where(x => !x.Ready && x.Kind != "fullstats")
                    .Select(x => x.Reason ?? "Данные источника не подтверждены.").ToArray(),
                StoreSpend: storeSpend, StoreOrderSum: storeOrders,
                TotalClicks: stats.Sum(x => (long)x.Clicks), TotalImpressions: stats.Sum(x => (long)x.Impressions),
                Previous: new TelegramPrevious(previousStoreSpend, previousStoreOrders, previousAdSpend, previousAdRevenue)));

            byte[]? chart = null;
            if (SourceReady(ready, "expenses") && SourceReady(ready, "orders"))
                chart = await BuildChartAsync(store.Id, yesterday, ct);
            return (Readiness: ready, Message: (string?)text, Chart: chart);
        }, ct);
        var readiness = prepared.Readiness;
        var message = prepared.Message;
        var chartPng = prepared.Chart;
        if (message == null) return new TelegramDailyResult(yesterday, false, readiness, 0, 0, 0, 0);
        var sent = 0;
        var skipped = 0;
        var failed = 0;
        var unknown = 0;
        // One instance serves the app, so a process-wide lock is enough to keep two quick clicks from racing.
        await SendLock.WaitAsync(ct);
        try
        {
            foreach (var chat in chats)
            {
                var delivery = await db.TelegramDeliveries.FirstOrDefaultAsync(x => x.StoreId == store.Id &&
                    x.ChatId == chat.Id && x.ReportDate == yesterday, ct);
                if (delivery == null)
                {
                    delivery = new TelegramDelivery
                    {
                        Id = Guid.NewGuid(), StoreId = store.Id, ChatId = chat.Id, ReportDate = yesterday,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    db.TelegramDeliveries.Add(delivery);
                }
                else if (delivery.Status == "sent") { skipped++; continue; }
                else if (delivery.Status == "unknown") { unknown++; continue; }
                else if (delivery.Status == "pending")
                {
                    // Fresh pending: someone is sending right now. Old pending: the process died mid-send,
                    // so Telegram may or may not have delivered it. Never resend automatically.
                    if (delivery.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(-5)) { skipped++; continue; }
                    delivery.Status = "unknown";
                    delivery.ErrorCode = "stale_pending";
                    delivery.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    unknown++;
                    continue;
                }
                else if (delivery.Status == "failed" && automatic && delivery.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(-15))
                {
                    // Automatic runs retry a confirmed failure at most every 15 minutes.
                    skipped++;
                    continue;
                }
                // Reaching here: new delivery, or a confirmed failure that is safe to retry.
                delivery.Status = "pending";
                delivery.ErrorCode = null;
                delivery.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                try
                {
                    await bot.SendHtmlAsync(chat.ChatId, message, ct);
                    delivery.Status = "sent";
                    sent++;
                    if (chartPng != null)
                    {
                        // The picture is a bonus: the summary is already delivered, so a failure here changes nothing.
                        try { await bot.SendPhotoAsync(chat.ChatId, chartPng, TelegramChartRenderer.Caption, ct); }
                        catch (Exception) when (!ct.IsCancellationRequested) { }
                    }
                }
                catch (TelegramApiException error)
                {
                    delivery.Status = "failed";
                    delivery.ErrorCode = $"telegram_{error.StatusCode}";
                    if (error.StatusCode == 403) chat.IsActive = false; // the user blocked the bot
                    failed++;
                }
                catch (Exception error) when (error is HttpRequestException ||
                    (error is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    delivery.Status = "unknown";
                    delivery.ErrorCode = "transport_unknown";
                    unknown++;
                }
                delivery.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            SendLock.Release();
        }
        return new TelegramDailyResult(yesterday, true, readiness, sent, skipped, failed, unknown);
    }

    /// <summary>Spend and DRR (spend / orders) for the 7 days up to the report date and the 7 days before them.</summary>
    private async Task<byte[]?> BuildChartAsync(Guid storeId, DateOnly last, CancellationToken ct)
    {
        var first = last.AddDays(-13);
        var spends = await db.WbStoreDailySpends.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Date >= first && x.Date <= last)
            .Select(x => new { x.Date, x.Spend }).ToDictionaryAsync(x => x.Date, x => (decimal)x.Spend, ct);
        var orders = await db.WbStoreDailyOrders.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Date >= first && x.Date <= last)
            .Select(x => new { x.Date, x.OrderSum }).ToDictionaryAsync(x => x.Date, x => (decimal)x.OrderSum, ct);
        decimal? Spend(DateOnly d) => spends.TryGetValue(d, out var v) ? v : null;
        decimal? Drr(DateOnly d) => spends.TryGetValue(d, out var s) && orders.TryGetValue(d, out var o) && o > 0 ? s * 100m / o : null;
        var days = Enumerable.Range(0, 7).Select(i => last.AddDays(i - 6)).ToList();
        var before = days.Select(d => d.AddDays(-7)).ToList();
        var current = days.Select(Spend).ToList();
        if (current.Count(x => x.HasValue) < 2) return null;
        return TelegramChartRenderer.Render(days, current, days.Select(Drr).ToList(),
            before.Select(Spend).ToList(), before.Select(Drr).ToList());
    }

    private static bool SourceReady(WbReportReadiness readiness, string kind) =>
        readiness.Sources.Any(x => x.Kind == kind && x.Ready);

    private Task<T> ReadReportAsync<T>(Func<Task<T>> prepare, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var result = await prepare();
            await tx.CommitAsync(ct);
            return result;
        });

    private static string? MoscowText(DateTime? utc) => utc.HasValue
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")).ToString("dd.MM.yyyy HH:mm") + " МСК"
        : null;
}

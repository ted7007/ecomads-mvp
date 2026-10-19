using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Telegram;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/telegram")]
public sealed class TelegramController(EcomadsDbContext db, ITelegramBotClient bot, WbReportReadinessService reportReadiness) : ControllerBase
{
    private static readonly SemaphoreSlim SendLock = new(1, 1);

    [HttpGet("chats")]
    public async Task<IActionResult> Chats(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var chats = await db.TelegramChats.AsNoTracking()
            .Where(x => x.SellerId == sellerId && x.IsActive)
            .OrderBy(x => x.LinkedAtUtc)
            .Select(x => new { x.Id, x.DisplayName, x.LinkedAtUtc })
            .ToListAsync(cancellationToken);
        return Ok(new { botConfigured = bot.IsConfigured, botUsername = bot.BotUsername, chats });
    }

    [HttpPost("link")]
    public async Task<IActionResult> CreateLink(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!bot.IsConfigured) return Conflict(new { message = "Telegram-бот ещё не подключён к сервису. Обратитесь к администратору." });
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));
        var expires = DateTime.UtcNow.AddMinutes(15);
        db.TelegramLinkCodes.Add(new TelegramLinkCode
        {
            Id = Guid.NewGuid(), SellerId = sellerId, CodeHash = hash, ExpiresAtUtc = expires
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            code,
            linkUrl = string.IsNullOrWhiteSpace(bot.BotUsername)
                ? null : $"https://t.me/{bot.BotUsername}?start={code}",
            expiresAtUtc = expires
        });
    }

    [HttpDelete("chats/{chatId:guid}")]
    public async Task<IActionResult> Disconnect(Guid chatId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var chat = await db.TelegramChats.SingleOrDefaultAsync(x => x.Id == chatId && x.SellerId == sellerId, cancellationToken);
        if (chat == null) return NotFound();
        chat.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("stores/{storeId:guid}/send-yesterday")]
    public async Task<IActionResult> SendYesterday(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!bot.IsConfigured) return Conflict(new { message = "Telegram-бот ещё не подключён к сервису. Обратитесь к администратору." });
        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId &&
            x.SellerId == sellerId && x.ApiKey != null, cancellationToken);
        if (store == null) return NotFound();
        var chats = await db.TelegramChats.Where(x => x.SellerId == sellerId && x.IsActive)
            .ToListAsync(cancellationToken);
        if (chats.Count == 0) return Conflict(new { message = "Сначала привяжите чат Telegram: нажмите «Создать ссылку для привязки», откройте бота по ссылке и нажмите Start." });

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var prepared = await ReadReportAsync(async () =>
        {
            var ready = await reportReadiness.GetAsync(storeId, yesterday, cancellationToken);
            if (!ready.Collected) return (Readiness: ready, Message: (string?)null);
            var day = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId &&
                ready.ConfirmedCampaignIds.Contains(x.Id))
                .Select(x => new { x.Id, x.WbCampaignId, x.Name }).ToListAsync(cancellationToken);

            var campaignIds = campaigns.Select(x => x.Id).ToArray();
            var stats = await db.CampaignStatistics.AsNoTracking()
                .Where(x => campaignIds.Contains(x.CampaignId) && x.Date == day)
                .Select(x => new { x.CampaignId, x.Spend, x.Revenue, x.Orders })
                .ToListAsync(cancellationToken);

            var campaignTargets = await db.WbCampaignNorms.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId))
                .Select(x => new { x.CampaignId, x.TargetDrr, x.CustomName }).ToDictionaryAsync(x => x.CampaignId, cancellationToken);
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
                await db.WbStoreDailySpends.Where(x => x.StoreId == storeId && x.Date == yesterday)
                    .Select(x => (decimal?)x.Spend).SingleOrDefaultAsync(cancellationToken) : null;
            decimal? storeOrders = ready.Sources.Any(x => x.Kind == "orders" && x.Ready) ?
                await db.WbStoreDailyOrders.Where(x => x.StoreId == storeId && x.Date == yesterday)
                    .Select(x => (decimal?)x.OrderSum).SingleOrDefaultAsync(cancellationToken) : null;
            var checkedAt = ready.Sources.Where(x => x.CheckedAtUtc.HasValue).Min(x => x.CheckedAtUtc);
            var text = TelegramSummaryFormatter.Format(new TelegramSummary(store.Name, yesterday,
                totalSpend, totalRevenue, stats.Sum(x => (long)x.Orders),
                rows.Select(x => new TelegramSummaryCampaign(x.Name, x.Spend, x.Orders, x.Drr)).ToArray(),
                MoscowText(checkedAt), DashboardUrl(yesterday),
                MissingCampaignNames: ready.MissingCampaigns.Select(x => $"{x.Name} (ID {x.WbCampaignId})").ToArray(),
                SourceWarnings: ready.Sources.Where(x => !x.Ready && x.Kind != "fullstats")
                    .Select(x => x.Reason ?? "Данные источника не подтверждены.").ToArray(),
                StoreSpend: storeSpend, StoreOrderSum: storeOrders));

            return (Readiness: ready, Message: (string?)text);
        }, cancellationToken);
        var readiness = prepared.Readiness;
        var message = prepared.Message;
        if (message == null)
            return Conflict(new { message = "Сбор за вчера ещё не завершён. Дождитесь проверки рекламы, расхода и всех заказов после окончания дня.", readiness });
        var sent = 0;
        var skipped = 0;
        var failed = 0;
        var unknown = 0;
        // One instance serves the app, so a process-wide lock is enough to keep two quick clicks from racing.
        await SendLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var chat in chats)
            {
                var delivery = await db.TelegramDeliveries.FirstOrDefaultAsync(x => x.StoreId == storeId &&
                    x.ChatId == chat.Id && x.ReportDate == yesterday, cancellationToken);
                if (delivery == null)
                {
                    delivery = new TelegramDelivery
                    {
                        Id = Guid.NewGuid(), StoreId = storeId, ChatId = chat.Id, ReportDate = yesterday,
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
                    await db.SaveChangesAsync(cancellationToken);
                    unknown++;
                    continue;
                }
                // Reaching here: new delivery, or a confirmed failure that is safe to retry.
                delivery.Status = "pending";
                delivery.ErrorCode = null;
                delivery.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                try
                {
                    await bot.SendMessageAsync(chat.ChatId, message, cancellationToken);
                    delivery.Status = "sent";
                    sent++;
                }
                catch (TelegramApiException error)
                {
                    delivery.Status = "failed";
                    delivery.ErrorCode = $"telegram_{error.StatusCode}";
                    if (error.StatusCode == 403) chat.IsActive = false; // the user blocked the bot
                    failed++;
                }
                catch (Exception error) when (error is HttpRequestException ||
                    (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    delivery.Status = "unknown";
                    delivery.ErrorCode = "transport_unknown";
                    unknown++;
                }
                delivery.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            SendLock.Release();
        }
        return Ok(new { reportDate = yesterday, sent, skipped, failed, unknown, partial = !readiness.Ready,
            missingCampaigns = readiness.MissingCampaigns.Count });
    }

    /// <summary>Sends clearly labeled demo data to the linked chats. No WB cabinet needed, nothing is stored.</summary>
    [HttpPost("send-test")]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!bot.IsConfigured) return Conflict(new { message = "Telegram-бот ещё не подключён к сервису. Обратитесь к администратору." });
        var chats = await db.TelegramChats.Where(x => x.SellerId == sellerId && x.IsActive)
            .ToListAsync(cancellationToken);
        if (chats.Count == 0) return Conflict(new { message = "Сначала привяжите чат Telegram: нажмите «Создать ссылку для привязки», откройте бота по ссылке и нажмите Start." });

        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var demo = new List<TelegramSummaryCampaign>
        {
            new("Демо: поиск, бренд", 12450.50m, 31, 14.2m),
            new("Демо: автоматическая кампания с очень длинным названием для проверки переноса", 8300m, 12, 36.8m),
            new("Демо: без выручки", 1200m, 0, null)
        };
        var message = TelegramSummaryFormatter.Format(new TelegramSummary("Демо-кабинет", day,
            demo.Sum(x => x.Spend), 87650m, demo.Sum(x => x.Orders), demo,
            MoscowText(DateTime.UtcNow), DashboardUrl(day), IsTest: true));

        var sent = 0;
        var failed = 0;
        foreach (var chat in chats)
        {
            try
            {
                await bot.SendMessageAsync(chat.ChatId, message, cancellationToken);
                sent++;
            }
            catch (TelegramApiException error)
            {
                if (error.StatusCode == 403) chat.IsActive = false;
                failed++;
            }
            catch (Exception error) when (error is HttpRequestException ||
                (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                failed++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { sent, failed });
    }

    [HttpPost("stores/{storeId:guid}/send-period")]
    public async Task<IActionResult> SendPeriod(Guid storeId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!bot.IsConfigured) return Conflict(new { message = "Telegram-бот ещё не подключён к сервису. Обратитесь к администратору." });
        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId &&
            x.SellerId == sellerId && x.ApiKey != null, cancellationToken);
        if (store == null) return NotFound();
        var chats = await db.TelegramChats.Where(x => x.SellerId == sellerId && x.IsActive)
            .ToListAsync(cancellationToken);
        if (chats.Count == 0) return Conflict(new { message = "Сначала привяжите чат Telegram: нажмите «Создать ссылку для привязки», откройте бота по ссылке и нажмите Start." });

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        if (startDate == default || endDate == default || startDate > endDate)
            return Conflict(new { message = "Выберите период: дата «с» не должна быть позже даты «по»." });
        if (endDate > yesterday)
            return Conflict(new { message = $"Сводку можно отправить только за завершённые дни: последняя доступная дата {yesterday:dd.MM.yyyy}." });
        if (endDate.DayNumber - startDate.DayNumber >= MaxPeriodDays)
            return Conflict(new { message = $"Период слишком длинный: можно отправить не больше {MaxPeriodDays} дней за раз." });

        // Same readiness rules as the daily summary, checked per day. WB may legitimately not return some campaigns:
        // those are listed in the message, never counted as zeros.
        var prepared = await ReadReportAsync(async () =>
        {
            var notCollected = new List<DateOnly>();
            var sourceWarnings = new HashSet<string>();
            var confirmedIds = new HashSet<Guid>();
            var confirmedDays = new HashSet<(Guid CampaignId, DateOnly Date)>();
            var missing = new Dictionary<Guid, string>();
            var expensesReady = true; var ordersReady = true;
            DateTime? checkedAt = null;
            for (var d = startDate; d <= endDate; d = d.AddDays(1))
            {
                var day = await reportReadiness.GetAsync(storeId, d, cancellationToken);
                if (!day.Collected) { notCollected.Add(d); continue; }
                expensesReady &= day.Sources.Any(x => x.Kind == "expenses" && x.Ready);
                ordersReady &= day.Sources.Any(x => x.Kind == "orders" && x.Ready);
                foreach (var source in day.Sources.Where(x => !x.Ready && x.Kind != "fullstats"))
                    sourceWarnings.Add(source.Reason ?? "Данные источника не подтверждены.");
                confirmedIds.UnionWith(day.ConfirmedCampaignIds);
                foreach (var id in day.ConfirmedCampaignIds) confirmedDays.Add((id, d));
                foreach (var m in day.MissingCampaigns) missing[m.CampaignId] = $"{m.Name} (ID {m.WbCampaignId})";
                var dayChecked = day.Sources.Where(x => x.CheckedAtUtc.HasValue).Min(x => x.CheckedAtUtc);
                if (dayChecked.HasValue && (checkedAt == null || dayChecked < checkedAt)) checkedAt = dayChecked;
            }
            if (notCollected.Count > 0) return (Message: (string?)null, NotCollected: notCollected.ToArray(), MissingCount: missing.Count, Partial: true);
            var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId && confirmedIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
            var text = await BuildPeriodMessageAsync(store, campaigns, startDate, endDate, missing.Values.ToArray(),
                MoscowText(checkedAt), confirmedDays, sourceWarnings.ToArray(), expensesReady, ordersReady, cancellationToken);
            return (Message: (string?)text, NotCollected: Array.Empty<DateOnly>(), MissingCount: missing.Count, Partial: missing.Count > 0 || sourceWarnings.Count > 0);
        }, cancellationToken);
        var message = prepared.Message;
        if (message == null)
        {
            var dates = string.Join(", ", prepared.NotCollected.Take(5).Select(x => x.ToString("dd.MM")));
            if (prepared.NotCollected.Length > 5) dates += $" и ещё {prepared.NotCollected.Length - 5}";
            return Conflict(new { message = $"Сбор за часть периода ещё не завершён (дни: {dates}). " +
                "Дождитесь проверки рекламы, расхода и заказов после окончания дня или выберите более ранний период." });
        }
        // Period summaries are explicit one-off actions: no per-day duplicate protection and no history row.
        var sent = 0;
        var failed = 0;
        foreach (var chat in chats)
        {
            try
            {
                await bot.SendMessageAsync(chat.ChatId, message, cancellationToken);
                sent++;
            }
            catch (TelegramApiException error)
            {
                if (error.StatusCode == 403) chat.IsActive = false;
                failed++;
            }
            catch (Exception error) when (error is HttpRequestException ||
                (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                failed++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { startDate, endDate, sent, failed, partial = prepared.Partial, missingCampaigns = prepared.MissingCount });
    }

    /// <summary>Daily deliveries grouped into events: one summary (cabinet + date) with every chat it went to.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var chatNames = await db.TelegramChats.AsNoTracking().Where(x => x.SellerId == sellerId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var chatIds = chatNames.Keys.ToList();
        var deliveries = await db.TelegramDeliveries.AsNoTracking().Where(x => chatIds.Contains(x.ChatId))
            .OrderByDescending(x => x.UpdatedAtUtc).Take(300).ToListAsync(cancellationToken);
        var storeNames = await db.Stores.AsNoTracking().Where(x => x.SellerId == sellerId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var events = deliveries.GroupBy(x => new { x.StoreId, x.ReportDate })
            .Select(g => new
            {
                storeName = storeNames.GetValueOrDefault(g.Key.StoreId) ?? "Кабинет",
                reportDate = g.Key.ReportDate,
                lastAtUtc = g.Max(x => x.UpdatedAtUtc),
                chats = g.OrderBy(x => x.CreatedAtUtc).Select(x => new
                {
                    name = chatNames.GetValueOrDefault(x.ChatId) ?? "Личный чат",
                    status = x.Status,
                    errorCode = x.ErrorCode
                }).ToList()
            })
            .OrderByDescending(x => x.lastAtUtc).Take(30).ToList();
        return Ok(events);
    }

    private const int MaxPeriodDays = 31;

    private Task<T> ReadReportAsync<T>(Func<Task<T>> prepare, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var result = await prepare();
            await tx.CommitAsync(ct);
            return result;
        });

    private async Task<string> BuildPeriodMessageAsync(Store store, IReadOnlyList<Campaign> campaigns,
        DateOnly from, DateOnly to, IReadOnlyList<string> missingNames, string? updatedAtText,
        IReadOnlySet<(Guid CampaignId, DateOnly Date)> confirmedDays, IReadOnlyList<string> sourceWarnings,
        bool expensesReady, bool ordersReady, CancellationToken cancellationToken)
    {
        var fromDay = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toDay = DateTime.SpecifyKind(to.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var campaignIds = campaigns.Select(x => x.Id).ToArray();
        var stats = await db.CampaignStatistics.AsNoTracking()
            .Where(x => campaignIds.Contains(x.CampaignId) && x.Date >= fromDay && x.Date <= toDay)
            .Select(x => new { x.CampaignId, x.Date, x.Spend, x.Revenue, x.Orders })
            .ToListAsync(cancellationToken);
        stats = stats.Where(x => confirmedDays.Contains((x.CampaignId, DateOnly.FromDateTime(x.Date)))).ToList();
        var names = await db.WbCampaignNorms.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId))
            .Select(x => new { x.CampaignId, x.CustomName }).ToDictionaryAsync(x => x.CampaignId, x => x.CustomName, cancellationToken);
        var rows = campaigns.Select(c =>
        {
            var own = stats.Where(s => s.CampaignId == c.Id).ToList();
            var spend = own.Sum(s => (decimal)s.Spend);
            var revenue = own.Sum(s => (decimal)s.Revenue);
            var orders = own.Sum(s => (long)s.Orders);
            decimal? drr = revenue > 0 ? spend * 100m / revenue : null;
            return new TelegramSummaryCampaign(names.GetValueOrDefault(c.Id) ?? c.Name, spend, orders, drr);
        }).OrderByDescending(x => x.Spend).ToList();
        var expenseRows = expensesReady ? await db.WbStoreDailySpends.AsNoTracking().Where(x => x.StoreId == store.Id && x.Date >= from && x.Date <= to)
            .Select(x => x.Spend).ToArrayAsync(cancellationToken) : [];
        var orderRows = ordersReady ? await db.WbStoreDailyOrders.AsNoTracking().Where(x => x.StoreId == store.Id && x.Date >= from && x.Date <= to)
            .Select(x => x.OrderSum).ToArrayAsync(cancellationToken) : [];
        var days = to.DayNumber - from.DayNumber + 1;
        return TelegramSummaryFormatter.Format(new TelegramSummary(
            store.Name, from, rows.Sum(x => x.Spend), stats.Sum(x => (decimal)x.Revenue), stats.Sum(x => (long)x.Orders),
            rows, updatedAtText, DashboardUrl(from, to), MissingCampaignNames: missingNames,
            ReportEndDate: to, DashboardLinkLabel: "Открыть сводку за этот период", SourceWarnings: sourceWarnings.ToArray(),
            StoreSpend: expenseRows.Length == days ? expenseRows.Sum() : null,
            StoreOrderSum: orderRows.Length == days ? orderRows.Sum() : null));
    }

    private static string? MoscowText(DateTime? utc) => utc.HasValue
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")).ToString("dd.MM.yyyy HH:mm") + " МСК"
        : null;

    private string DashboardUrl(DateOnly from, DateOnly to) =>
        $"{Request.Scheme}://{Request.Host}/dashboard?startDate={from:yyyy-MM-dd}&endDate={to:yyyy-MM-dd}";

    private string DashboardUrl(DateOnly day) =>
        $"{Request.Scheme}://{Request.Host}/dashboard?startDate={day:yyyy-MM-dd}&endDate={day:yyyy-MM-dd}";

    private bool TrySellerId(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);
}

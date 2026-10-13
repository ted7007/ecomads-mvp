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
        if (!bot.IsConfigured) return Conflict(new { message = "Токен Telegram-бота пока не настроен." });
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
        if (!bot.IsConfigured) return Conflict(new { message = "Токен Telegram-бота пока не настроен." });
        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId &&
            x.SellerId == sellerId && x.ApiKey != null, cancellationToken);
        if (store == null) return NotFound();
        var chats = await db.TelegramChats.Where(x => x.SellerId == sellerId && x.IsActive)
            .ToListAsync(cancellationToken);
        if (chats.Count == 0) return Conflict(new { message = "Сначала привяжите личный чат Telegram." });

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var readiness = await reportReadiness.GetAsync(storeId, yesterday, cancellationToken);
        if (!readiness.Collected)
            return Conflict(new { message = "Сбор за вчера ещё не завершён. Дождитесь проверки рекламы, расхода и всех заказов после окончания дня.", readiness });
        var day = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId &&
            readiness.ConfirmedCampaignIds.Contains(x.Id))
            .Select(x => new { x.Id, x.WbCampaignId, x.Name }).ToListAsync(cancellationToken);

        var campaignIds = campaigns.Select(x => x.Id).ToArray();
        var stats = await db.CampaignStatistics.AsNoTracking()
            .Where(x => campaignIds.Contains(x.CampaignId) && x.Date == day)
            .Select(x => new { x.CampaignId, x.Spend, x.Revenue, x.Orders })
            .ToListAsync(cancellationToken);
        if (campaigns.Any(x => !stats.Any(s => s.CampaignId == x.Id)))
            return Conflict(new { message = "Вчерашние данные по части кампаний отсутствуют. Сводка не отправлена." });

        var storeTarget = await db.WbStoreNorms.AsNoTracking().Where(x => x.StoreId == storeId)
            .Select(x => (decimal?)x.TargetDrr).FirstOrDefaultAsync(cancellationToken) ?? 20m;
        var campaignTargets = await db.WbCampaignNorms.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId))
            .Select(x => new { x.CampaignId, x.TargetDrr, x.CustomName }).ToDictionaryAsync(x => x.CampaignId, cancellationToken);
        var rows = campaigns.Select(c =>
        {
            var stat = stats.Single(s => s.CampaignId == c.Id);
            campaignTargets.TryGetValue(c.Id, out var custom);
            var target = custom?.TargetDrr ?? storeTarget;
            var drr = stat.Revenue > 0 ? (decimal?)((decimal)stat.Spend * 100m / (decimal)stat.Revenue) : null;
            return new { Name = custom?.CustomName ?? c.Name, stat.Spend, stat.Orders, Drr = drr, Target = target };
        }).OrderByDescending(x => x.Spend).ToList();
        var totalSpend = rows.Sum(x => (decimal)x.Spend);
        var totalRevenue = stats.Sum(x => (decimal)x.Revenue);
        var spend = await db.WbStoreDailySpends.AsNoTracking().SingleAsync(x => x.StoreId == storeId && x.Date == yesterday, cancellationToken);
        var orders = await db.WbStoreDailyOrders.AsNoTracking().SingleAsync(x => x.StoreId == storeId && x.Date == yesterday, cancellationToken);
        var checkedAt = readiness.Sources.Where(x => x.CheckedAtUtc.HasValue).Min(x => x.CheckedAtUtc);
        var message = TelegramSummaryFormatter.Format(new TelegramSummary(store.Name, yesterday,
            totalSpend, totalRevenue, stats.Sum(x => (long)x.Orders),
            rows.Select(x => new TelegramSummaryCampaign(x.Name, x.Spend, x.Orders, x.Drr)).ToArray(),
            checkedAt?.ToString("dd.MM.yyyy HH:mm"), DashboardUrl(yesterday),
            MissingCampaignNames: readiness.MissingCampaigns.Select(x => $"{x.Name} (ID {x.WbCampaignId})").ToArray(),
            StoreSpend: spend.Spend, StoreOrdersSum: orders.OrderSum));

        var sent = 0;
        var skipped = 0;
        var failed = 0;
        var unknown = 0;
        await SendLock.WaitAsync(cancellationToken);
        try
        {
        foreach (var chat in chats)
        {
            var delivery = await db.TelegramDeliveries.FirstOrDefaultAsync(x => x.StoreId == storeId &&
                x.ChatId == chat.Id && x.ReportDate == yesterday, cancellationToken);
            if (delivery == null)
            {
                delivery = new TelegramDelivery { Id = Guid.NewGuid(), StoreId = storeId, ChatId = chat.Id,
                    ReportDate = yesterday, CreatedAtUtc = DateTime.UtcNow };
                db.TelegramDeliveries.Add(delivery);
            }
            else if (delivery.Status == "sent") { skipped++; continue; }
            else if (delivery.Status == "unknown") { unknown++; continue; }
            else if (delivery.Status == "pending")
            {
                if (delivery.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(-5)) { skipped++; continue; }
                delivery.Status = "unknown";
                delivery.ErrorCode = "stale_pending";
                delivery.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                unknown++;
                continue;
            }
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
                if (error.StatusCode == 403) chat.IsActive = false;
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
        finally { SendLock.Release(); }
        return Ok(new { reportDate = yesterday, sent, skipped, failed, unknown, partial = !readiness.Ready,
            missingCampaigns = readiness.MissingCampaigns.Count });
    }

    [HttpPost("send-test")]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!bot.IsConfigured) return Conflict(new { message = "Telegram-бот ещё не подключён к сервису. Обратитесь к администратору." });
        var chats = await db.TelegramChats.Where(x => x.SellerId == sellerId && x.IsActive).ToListAsync(cancellationToken);
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
            DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm"), DashboardUrl(day), IsTest: true));
        var sent = 0;
        var failed = 0;
        foreach (var chat in chats)
        {
            try { await bot.SendMessageAsync(chat.ChatId, message, cancellationToken); sent++; }
            catch (TelegramApiException error)
            { if (error.StatusCode == 403) chat.IsActive = false; failed++; }
            catch (Exception error) when (error is HttpRequestException ||
                (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            { failed++; }
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { sent, failed });
    }

    private string DashboardUrl(DateOnly day) =>
        $"{Request.Scheme}://{Request.Host}/dashboard?startDate={day:yyyy-MM-dd}&endDate={day:yyyy-MM-dd}";

    private bool TrySellerId(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);
}

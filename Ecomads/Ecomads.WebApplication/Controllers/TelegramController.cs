using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/telegram")]
public sealed class TelegramController(EcomadsDbContext db, ITelegramBotClient bot) : ControllerBase
{
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
        var day = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId &&
            (x.WbStatus == 9 || x.WbStatus == 11))
            .Select(x => new { x.Id, x.WbCampaignId, x.Name }).ToListAsync(cancellationToken);
        if (campaigns.Count == 0) return Conflict(new { message = "Нет активных или приостановленных кампаний для сводки." });

        var jobs = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == storeId &&
            x.Kind == "fullstats" && x.Status == "completed" && x.StartDate <= yesterday && x.EndDate >= yesterday)
            .Select(x => x.CampaignIdsJson).ToListAsync(cancellationToken);
        var loadedIds = jobs.SelectMany(x => JsonSerializer.Deserialize<long[]>(x) ?? []).ToHashSet();
        if (campaigns.Any(x => !long.TryParse(x.WbCampaignId, out var id) || !loadedIds.Contains(id)))
            return Conflict(new { message = "Для вчерашней сводки сначала загрузите все активные и приостановленные кампании за вчера." });

        var campaignIds = campaigns.Select(x => x.Id).ToArray();
        var stats = await db.CampaignStatistics.AsNoTracking()
            .Where(x => campaignIds.Contains(x.CampaignId) && x.Date == day)
            .Select(x => new { x.CampaignId, x.Spend, x.Revenue, x.Orders })
            .ToListAsync(cancellationToken);
        if (campaigns.Any(x => !stats.Any(s => s.CampaignId == x.Id)))
            return Conflict(new { message = "Вчерашние данные по части кампаний отсутствуют. Сводка не отправлена." });

        var storeTarget = await db.WbStoreNorms.AsNoTracking().Where(x => x.StoreId == storeId)
            .Select(x => (decimal?)x.TargetDrr).FirstOrDefaultAsync(cancellationToken) ?? 30m;
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
        var aboveTarget = rows.Where(x => x.Drr > x.Target).Take(5).ToList();
        var totalSpend = rows.Sum(x => (decimal)x.Spend);
        var totalRevenue = stats.Sum(x => (decimal)x.Revenue);
        var totalDrr = totalRevenue > 0 ? $"{totalSpend * 100m / totalRevenue:F1}%" : "—";
        var lines = new List<string>
        {
            $"EcomAds · {store.Name} · {yesterday:dd.MM.yyyy}",
            $"Реклама: расход {totalSpend:N2} ₽, ДРР {totalDrr}.",
            $"Выше цели: {rows.Count(x => x.Drr > x.Target)} из {rows.Count} кампаний.",
            "",
            "Требуют внимания:"
        };
        lines.AddRange(aboveTarget.Count == 0 ? ["Превышений рекламного ДРР нет."] :
            aboveTarget.Select(x => $"• {x.Name}: ДРР {x.Drr:F1}% при цели {x.Target:F1}%."));
        lines.Add("");
        lines.Add("Кампании по расходу:");
        lines.AddRange(rows.Take(10).Select(x =>
            $"• {x.Name}: {x.Spend:N2} ₽, {x.Orders} заказов, ДРР {(x.Drr.HasValue ? $"{x.Drr:F1}%" : "—")}."));
        lines.Add($"Обновлено: {store.LastSyncAt?.ToString("dd.MM.yyyy HH:mm") ?? "неизвестно"} UTC.");
        lines.Add($"{Request.Scheme}://{Request.Host}/dashboard?startDate={yesterday:yyyy-MM-dd}&endDate={yesterday:yyyy-MM-dd}");
        var message = string.Join('\n', lines);

        var sent = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var chat in chats)
        {
            var existing = await db.TelegramDeliveries.SingleOrDefaultAsync(x => x.StoreId == storeId &&
                x.ChatId == chat.Id && x.ReportDate == yesterday, cancellationToken);
            if (existing != null) { skipped++; continue; }
            var delivery = new TelegramDelivery
            {
                Id = Guid.NewGuid(), StoreId = storeId, ChatId = chat.Id, ReportDate = yesterday,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            db.TelegramDeliveries.Add(delivery);
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
                failed++;
            }
            catch (HttpRequestException)
            {
                delivery.Status = "unknown";
                delivery.ErrorCode = "transport_unknown";
                failed++;
            }
            delivery.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok(new { reportDate = yesterday, sent, skipped, failed });
    }

    private bool TrySellerId(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);
}

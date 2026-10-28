using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Telegram;

public sealed class TelegramUpdatesWorker(IServiceScopeFactory scopes, ITelegramBotClient bot,
    ILogger<TelegramUpdatesWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!bot.IsConfigured)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (TelegramApiException error)
            {
                logger.LogWarning("Telegram polling failed with HTTP {StatusCode}", error.StatusCode);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or DbUpdateException or OperationCanceledException)
            {
                logger.LogWarning("Telegram polling failed: {ErrorType}", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var state = await db.TelegramBotStates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state == null)
        {
            state = new TelegramBotState();
            db.TelegramBotStates.Add(state);
            await db.SaveChangesAsync(cancellationToken);
        }
        var updates = await bot.GetUpdatesAsync(state.NextUpdateId, cancellationToken);
        foreach (var update in updates.OrderBy(x => x.UpdateId))
        {
            if (update.UpdateId < state.NextUpdateId) continue;
            try
            {
                await HandleAsync(db, update, cancellationToken);
            }
            catch (TelegramApiException error)
            {
                // One failing chat must not block the whole queue: the update is skipped, not retried forever.
                logger.LogWarning("Telegram update {UpdateId} was not answered: HTTP {StatusCode}",
                    update.UpdateId, error.StatusCode);
                if (error.StatusCode == 403) await DeactivateChatAsync(db, update.ChatId, cancellationToken);
            }
            catch (HttpRequestException)
            {
                logger.LogWarning("Telegram update {UpdateId} was not answered: transport error", update.UpdateId);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Telegram update {UpdateId} was not answered: timeout", update.UpdateId);
            }
            state.NextUpdateId = update.UpdateId + 1;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task DeactivateChatAsync(EcomadsDbContext db, long chatId, CancellationToken cancellationToken)
    {
        if (chatId == 0) return;
        var chat = await db.TelegramChats.SingleOrDefaultAsync(x => x.ChatId == chatId, cancellationToken);
        if (chat is not { IsActive: true }) return;
        chat.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleAsync(EcomadsDbContext db, TelegramIncoming update, CancellationToken cancellationToken)
    {
        if (!update.IsPrivate || update.ChatId == 0) return;
        var text = update.Text.Trim();
        if (text.StartsWith("/start ", StringComparison.OrdinalIgnoreCase))
        {
            var code = text[7..].Trim();
            if (code.Length != 32 || !code.All(Uri.IsHexDigit))
            {
                await bot.SendMessageAsync(update.ChatId, "Ссылка для привязки недействительна. Создайте новую в EcomAds.", cancellationToken);
                return;
            }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));
            var link = await db.TelegramLinkCodes.SingleOrDefaultAsync(x => x.CodeHash == hash &&
                x.UsedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow, cancellationToken);
            if (link == null)
            {
                await bot.SendMessageAsync(update.ChatId, "Ссылка истекла или уже использована. Создайте новую в EcomAds.", cancellationToken);
                return;
            }
            var chat = await db.TelegramChats.SingleOrDefaultAsync(x => x.ChatId == update.ChatId, cancellationToken);
            if (chat == null)
            {
                chat = new TelegramChat { Id = Guid.NewGuid(), ChatId = update.ChatId };
                db.TelegramChats.Add(chat);
            }
            chat.SellerId = link.SellerId;
            chat.DisplayName = update.DisplayName?.Length > 255 ? update.DisplayName[..255] : update.DisplayName;
            chat.LinkedAtUtc = DateTime.UtcNow;
            chat.IsActive = true;
            link.UsedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await bot.SendMessageAsync(update.ChatId, "Чат привязан к EcomAds. Сводка приходит только по кнопке в приложении. /stop — отключить чат.", cancellationToken);
            return;
        }

        if (text.Equals("/stop", StringComparison.OrdinalIgnoreCase))
        {
            var chat = await db.TelegramChats.SingleOrDefaultAsync(x => x.ChatId == update.ChatId, cancellationToken);
            if (chat != null)
            {
                chat.IsActive = false;
                await db.SaveChangesAsync(cancellationToken);
            }
            await bot.SendMessageAsync(update.ChatId, "Чат отключён от EcomAds.", cancellationToken);
            return;
        }

        if (text.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessageAsync(update.ChatId,
                "Для привязки откройте раздел «Кабинеты WB и Telegram» в EcomAds и получите персональную ссылку. /stop — отключить чат.", cancellationToken);
        }
    }
}

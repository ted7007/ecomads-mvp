using System.Net;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Telegram;

/// <summary>
/// The bot's button menu for a linked chat: yesterday's summary, 7-day summary, cabinets and switching messages on/off.
/// Everything here only reads data; nothing is recorded as a "delivery", so it never blocks the daily mailing.
/// Button data: m, y, w, c, on, off, h, y:&lt;storeId&gt;, w:&lt;storeId&gt; (at most 64 bytes for Telegram).
/// </summary>
public sealed class TelegramBotMenu(EcomadsDbContext db, ITelegramBotClient bot, TelegramDailySender sender,
    WbReportReadinessService readiness, IConfiguration configuration)
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    /// <summary>Returns true when the message was a menu command or a button press and has been answered.</summary>
    public async Task<bool> HandleAsync(TelegramIncoming update, CancellationToken ct)
    {
        var raw = update.Text.Trim();
        var data = update.CallbackId != null ? raw : raw.ToLowerInvariant() switch
        {
            "/menu" or "меню" => "m",
            "/summary" => "y",
            "/week" => "w",
            _ => null
        };
        if (data == null) return false;
        if (update.CallbackId != null)
        {
            try { await bot.AnswerCallbackAsync(update.CallbackId, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }

        var chat = await db.TelegramChats.SingleOrDefaultAsync(x => x.ChatId == update.ChatId, ct);
        if (chat == null)
        {
            await bot.SendMessageAsync(update.ChatId,
                "Чат ещё не привязан. Откройте раздел «Кабинеты WB и Telegram» в EcomAds, получите персональную ссылку и нажмите Start.", ct);
            return true;
        }

        switch (data)
        {
            case "on":
                chat.IsActive = true;
                await db.SaveChangesAsync(ct);
                await ShowMenuAsync(chat, "🔔 Сообщения включены. Сводка будет приходить после завершения загрузки данных за день.", ct);
                return true;
            case "off":
                chat.IsActive = false;
                await db.SaveChangesAsync(ct);
                await ShowMenuAsync(chat, "🔕 Сообщения отключены. Вернуть их можно кнопкой ниже.", ct);
                return true;
        }
        if (!chat.IsActive)
        {
            await ShowMenuAsync(chat, null, ct);
            return true;
        }

        var stores = await db.Stores.AsNoTracking().Where(x => x.SellerId == chat.SellerId && x.ApiKey != null)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        switch (data)
        {
            case "m":
                await ShowMenuAsync(chat, null, ct);
                break;
            case "h":
                await bot.SendMenuAsync(chat.ChatId,
                    "❓ <b>Как пользоваться ботом</b>\n\n" +
                    "• <b>Сводка за вчера</b> — расход, заказы, ДРР и топ кампаний.\n" +
                    "• <b>За 7 дней</b> — итоги недели и график.\n" +
                    "• <b>Мои кабинеты</b> — какие кабинеты подключены.\n" +
                    "• Сводка за вчера также приходит сама, когда данные загружены.\n\n" +
                    "Команды: /menu — меню, /summary — вчера, /stop — отключить сообщения.", Back(), ct);
                break;
            case "c":
                await bot.SendMenuAsync(chat.ChatId, stores.Count == 0
                    ? "🏪 Кабинетов с подключённым ключом WB пока нет. Добавьте ключ в EcomAds."
                    : "🏪 <b>Мои кабинеты</b>\n" + string.Join("\n", stores.Select(x => "• " + WebUtility.HtmlEncode(x.Name))), Back(), ct);
                break;
            case "y" or "w":
                if (stores.Count == 0)
                    await bot.SendMenuAsync(chat.ChatId, "Кабинетов с подключённым ключом WB пока нет. Добавьте ключ в EcomAds.", Back(), ct);
                else if (stores.Count == 1)
                    await SendReportAsync(chat, stores[0].Id, data == "w", ct);
                else
                    await bot.SendMenuAsync(chat.ChatId, "Выберите кабинет:", stores.Take(8)
                        .Select(x => (IReadOnlyList<TelegramButton>)[new TelegramButton(Short(x.Name), Data: $"{data}:{x.Id:N}")])
                        .Append((IReadOnlyList<TelegramButton>)[new TelegramButton("⬅️ Меню", Data: "m")]).ToList(), ct);
                break;
            default:
                if (data.Length == 34 && data[1] == ':' && (data[0] == 'y' || data[0] == 'w') &&
                    Guid.TryParseExact(data[2..], "N", out var storeId) && stores.Any(x => x.Id == storeId))
                    await SendReportAsync(chat, storeId, data[0] == 'w', ct);
                else
                    await ShowMenuAsync(chat, null, ct);
                break;
        }
        return true;
    }

    private async Task ShowMenuAsync(TelegramChat chat, string? note, CancellationToken ct)
    {
        var text = (note == null ? string.Empty : note + "\n\n") + "📋 <b>Меню EcomAds</b>\nЧто показать?";
        IReadOnlyList<IReadOnlyList<TelegramButton>> buttons;
        if (chat.IsActive)
            buttons =
            [
                [new TelegramButton("📊 Сводка за вчера", Data: "y"), new TelegramButton("📈 За 7 дней", Data: "w")],
                [new TelegramButton("🏪 Мои кабинеты", Data: "c"), new TelegramButton("❓ Помощь", Data: "h")],
                [new TelegramButton("🌐 Открыть EcomAds", Url: BaseUrl()), new TelegramButton("🔕 Отключить сообщения", Data: "off")]
            ];
        else
            buttons = [[new TelegramButton("🔔 Включить сообщения", Data: "on")]];
        await bot.SendMenuAsync(chat.ChatId, text, buttons, ct);
    }

    private async Task SendReportAsync(TelegramChat chat, Guid storeId, bool week, CancellationToken ct)
    {
        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == chat.SellerId, ct);
        if (store == null) { await ShowMenuAsync(chat, null, ct); return; }
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Moscow).DateTime).AddDays(-1);
        if (week)
        {
            await SendWeekAsync(chat, store, yesterday, ct);
            return;
        }
        var url = $"{BaseUrl()}/dashboard?startDate={yesterday:yyyy-MM-dd}&endDate={yesterday:yyyy-MM-dd}";
        var prepared = await sender.PrepareAsync(store, yesterday, url, ct);
        if (prepared.Html == null)
        {
            await bot.SendMenuAsync(chat.ChatId,
                $"⏳ Данные за {yesterday:dd.MM.yyyy} ещё загружаются. Они появятся, когда WB отдаст рекламу, расход и заказы за весь день. Попробуйте позже.", Back(), ct);
            return;
        }
        await SendCardAsync(chat.ChatId, prepared.Card, ct);
        await bot.SendMenuAsync(chat.ChatId, prepared.Html, TelegramDailySender.SummaryButtons(url), ct);
    }

    private async Task SendWeekAsync(TelegramChat chat, Store store, DateOnly last, CancellationToken ct)
    {
        var days = Enumerable.Range(0, 7).Select(i => last.AddDays(i - 6)).ToList();
        var complete = true;
        foreach (var day in days)
        {
            var state = await readiness.GetAsync(store.Id, day, ct);
            if (!state.Collected || !state.Sources.Any(x => x.Kind == "expenses" && x.Ready) ||
                !state.Sources.Any(x => x.Kind == "orders" && x.Ready)) complete = false;
        }
        var series = await sender.LoadSeriesAsync(store.Id, last, ct);
        var url = $"{BaseUrl()}/dashboard?startDate={days[0]:yyyy-MM-dd}&endDate={last:yyyy-MM-dd}";
        var spend = complete && series.Spend.All(x => x.HasValue) ? series.Spend.Sum(x => x!.Value) : (decimal?)null;
        var orderSum = complete ? await db.WbStoreDailyOrders.AsNoTracking()
            .Where(x => x.StoreId == store.Id && x.Date >= days[0] && x.Date <= last)
            .Select(x => (decimal)x.OrderSum).SumAsync(ct) : (decimal?)null;
        var drr = spend.HasValue && orderSum > 0 ? spend / orderSum * 100m : null;
        var label = $"{days[0].Day}–{TelegramDailySender.RussianDate(last)}";
        var text = $"📈 <b>EcomAds · {WebUtility.HtmlEncode(store.Name)}</b>\n<blockquote>Сводка за 7 дней: {days[0]:dd.MM} – {last:dd.MM.yyyy}, МСК</blockquote>\n\n" +
            (complete
                ? $"Расход кабинета: <b>{TelegramSummaryFormatter.FormatMoney(spend ?? 0)} ₽</b>\n" +
                  $"Все заказы, сумма: <b>{TelegramSummaryFormatter.FormatMoney(orderSum ?? 0)} ₽</b>\n" +
                  $"Общий ДРР: <b>{TelegramSummaryFormatter.FormatDrr(drr)}</b>"
                : "⚠️ Данные подтверждены не за все 7 дней, поэтому итоги за неделю не считаем — пропуски не равны нулю. На графике только подтверждённые дни.");
        var card = new TelegramCardData(store.Name, "Реклама за 7 дней", label, null,
            spend, null, orderSum, null, drr, null, null, series.Days, series.Spend, series.Drr, series.PreviousDrr, []);
        await SendCardAsync(chat.ChatId, complete || series.Spend.Count(x => x.HasValue) >= 2 ? card : null, ct);
        await bot.SendMenuAsync(chat.ChatId, text, TelegramDailySender.SummaryButtons(url), ct);
    }

    private async Task SendCardAsync(long chatId, TelegramCardData? card, CancellationToken ct)
    {
        if (card == null) return;
        var picture = TelegramCardRenderer.RenderOrFallback(card);
        if (picture == null) return;
        try { await bot.SendPhotoAsync(chatId, picture, $"<b>{WebUtility.HtmlEncode(card.StoreName)}</b> · {card.Title.ToLowerInvariant()}, {card.DateLabel}", ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { }
    }

    private static IReadOnlyList<IReadOnlyList<TelegramButton>> Back() => [[new TelegramButton("⬅️ Меню", Data: "m")]];

    private static string Short(string name) => name.Length <= 40 ? name : name[..39] + "…";

    private string BaseUrl() => (configuration["Telegram:DashboardBaseUrl"] ?? "https://ecomads.ru").TrimEnd('/');
}

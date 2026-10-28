using System.Net.Http.Json;
using System.Text.Json;

namespace Ecomads.WebApplication.Services.Telegram;

/// <summary>CallbackId is set when the update is a button press; Text then holds the button's data.</summary>
public sealed record TelegramIncoming(long UpdateId, long ChatId, string Text, string? DisplayName, bool IsPrivate,
    string? CallbackId = null);

/// <summary>An inline button: either a link (Url) or a press that comes back to the bot (Data).</summary>
public sealed record TelegramButton(string Text, string? Url = null, string? Data = null);

public interface ITelegramBotClient
{
    bool IsConfigured { get; }
    string? BotUsername { get; }
    Task<IReadOnlyList<TelegramIncoming>> GetUpdatesAsync(long offset, CancellationToken cancellationToken);
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);

    /// <summary>Sends text formatted with Telegram HTML tags. Implementations without HTML support get plain text.</summary>
    Task SendHtmlAsync(long chatId, string html, CancellationToken cancellationToken) =>
        SendMessageAsync(chatId, TelegramHtml.ToPlain(html), cancellationToken);

    /// <summary>Sends a PNG picture with a caption. Implementations without picture support skip it.</summary>
    Task SendPhotoAsync(long chatId, byte[] png, string caption, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Sends HTML text with rows of inline buttons. Implementations without buttons send the text only.</summary>
    Task SendMenuAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>> buttons,
        CancellationToken cancellationToken) => SendHtmlAsync(chatId, html, cancellationToken);

    /// <summary>Stops the spinner on a pressed button.</summary>
    Task AnswerCallbackAsync(string callbackId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Fills the "Menu" list next to the message box.</summary>
    Task SetCommandsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class TelegramBotClient(IConfiguration configuration) : ITelegramBotClient
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.telegram.org"),
        Timeout = TimeSpan.FromSeconds(40) };
    private readonly string? _token = configuration["Telegram:BotToken"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);
    public string? BotUsername => configuration["Telegram:BotUsername"]?.TrimStart('@');

    public async Task<IReadOnlyList<TelegramIncoming>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return [];
        using var response = await Http.PostAsJsonAsync($"/bot{_token}/getUpdates", new
        {
            offset, timeout = 20, limit = 50, allowed_updates = new[] { "message", "callback_query" }
        }, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new TelegramApiException((int)response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean() ||
            !json.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            throw new TelegramApiException((int)response.StatusCode);
        var updates = new List<TelegramIncoming>();
        foreach (var update in result.EnumerateArray())
        {
            var updateId = update.GetProperty("update_id").GetInt64();
            if (update.TryGetProperty("callback_query", out var callback))
            {
                var callbackId = callback.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                var data = callback.TryGetProperty("data", out var dataValue) ? dataValue.GetString() ?? string.Empty : string.Empty;
                if (callbackId != null && callback.TryGetProperty("message", out var source) &&
                    source.TryGetProperty("chat", out var sourceChat))
                {
                    var from = callback.TryGetProperty("from", out var user) && user.TryGetProperty("first_name", out var firstName)
                        ? firstName.GetString() : null;
                    var isPrivateChat = sourceChat.TryGetProperty("type", out var chatType) && chatType.GetString() == "private";
                    updates.Add(new TelegramIncoming(updateId, sourceChat.GetProperty("id").GetInt64(), data, from, isPrivateChat, callbackId));
                    continue;
                }
                updates.Add(new TelegramIncoming(updateId, 0, string.Empty, null, false));
                continue;
            }
            if (!update.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("chat", out var chat))
            {
                updates.Add(new TelegramIncoming(updateId, 0, string.Empty, null, false));
                continue;
            }
            var chatId = chat.GetProperty("id").GetInt64();
            var isPrivate = chat.TryGetProperty("type", out var type) && type.GetString() == "private";
            var name = chat.TryGetProperty("first_name", out var first) ? first.GetString() : null;
            var text = message.TryGetProperty("text", out var value) ? value.GetString() ?? string.Empty : string.Empty;
            updates.Add(new TelegramIncoming(updateId, chatId, text, name, isPrivate));
        }
        return updates;
    }

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken) =>
        SendAsync(chatId, text, null, cancellationToken);

    public async Task SendHtmlAsync(long chatId, string html, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(chatId, html, "HTML", cancellationToken);
        }
        catch (TelegramApiException error) when (error.StatusCode == 400)
        {
            // Telegram rejected the markup (nothing was delivered): send the same content as plain text.
            await SendAsync(chatId, TelegramHtml.ToPlain(html), null, cancellationToken);
        }
    }

    public async Task SendPhotoAsync(long chatId, byte[] png, string caption, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new TelegramApiException(0);
        var html = caption.Length <= 1024;
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId.ToString()), "chat_id" },
            { new StringContent(html ? caption : TelegramHtml.ToPlain(caption)[..Math.Min(1024, TelegramHtml.ToPlain(caption).Length)]), "caption" }
        };
        if (html) form.Add(new StringContent("HTML"), "parse_mode");
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "chart.png");
        using var response = await Http.PostAsync($"/bot{_token}/sendPhoto", form, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new TelegramApiException((int)response.StatusCode);
    }

    public async Task SendMenuAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>> buttons,
        CancellationToken cancellationToken)
    {
        var markup = new
        {
            inline_keyboard = buttons.Select(row => row.Select(b => b.Url != null
                ? (object)new { text = b.Text, url = b.Url } : new { text = b.Text, callback_data = b.Data ?? "m" }).ToArray()).ToArray()
        };
        try
        {
            await SendAsync(chatId, html, "HTML", cancellationToken, markup);
        }
        catch (TelegramApiException error) when (error.StatusCode == 400)
        {
            // Telegram refused the markup or a button link (nothing was delivered): send the text on its own.
            await SendHtmlAsync(chatId, html, cancellationToken);
        }
    }

    public async Task AnswerCallbackAsync(string callbackId, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return;
        using var response = await Http.PostAsJsonAsync($"/bot{_token}/answerCallbackQuery",
            new { callback_query_id = callbackId }, cancellationToken);
    }

    public async Task SetCommandsAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) return;
        using var response = await Http.PostAsJsonAsync($"/bot{_token}/setMyCommands", new
        {
            commands = new[]
            {
                new { command = "menu", description = "Меню: сводки и настройки" },
                new { command = "summary", description = "Сводка за вчера" },
                new { command = "help", description = "Как пользоваться ботом" },
                new { command = "stop", description = "Отключить сообщения" }
            }
        }, cancellationToken);
    }

    private async Task SendAsync(long chatId, string text, string? parseMode, CancellationToken cancellationToken,
        object? replyMarkup = null)
    {
        if (!IsConfigured) throw new TelegramApiException(0);
        using var response = await Http.PostAsJsonAsync($"/bot{_token}/sendMessage", new
        {
            chat_id = chatId, text, parse_mode = parseMode, disable_web_page_preview = true, reply_markup = replyMarkup
        }, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new TelegramApiException((int)response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new TelegramApiException((int)response.StatusCode);
    }
}

public sealed class TelegramApiException(int statusCode) : Exception($"Telegram API HTTP {statusCode}")
{
    public int StatusCode { get; } = statusCode;
}

public static class TelegramHtml
{
    /// <summary>Removes Telegram HTML tags and decodes entities, so formatted text can be sent as plain text.</summary>
    public static string ToPlain(string html) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty));
}

using System.Net.Http.Json;
using System.Text.Json;

namespace Ecomads.WebApplication.Services.Telegram;

public sealed record TelegramIncoming(long UpdateId, long ChatId, string Text, string? DisplayName, bool IsPrivate);

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
            offset, timeout = 20, limit = 50, allowed_updates = new[] { "message" }
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
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId.ToString()), "chat_id" },
            { new StringContent(caption.Length > 1024 ? caption[..1024] : caption), "caption" }
        };
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "chart.png");
        using var response = await Http.PostAsync($"/bot{_token}/sendPhoto", form, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new TelegramApiException((int)response.StatusCode);
    }

    private async Task SendAsync(long chatId, string text, string? parseMode, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new TelegramApiException(0);
        using var response = await Http.PostAsJsonAsync($"/bot{_token}/sendMessage", new
        {
            chat_id = chatId, text, parse_mode = parseMode, disable_web_page_preview = true
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

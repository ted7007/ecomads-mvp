using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbTokenClaims(string SellerId, DateTime ExpiresAtUtc, bool HasAnalytics, bool HasStatistics);

public interface IWbTokenService
{
    WbTokenClaims ReadClaims(string token);
    string Protect(string token);
    string Unprotect(string protectedToken);
}

public sealed class WbTokenService(IDataProtectionProvider provider) : IWbTokenService
{
    private readonly IDataProtector _protector = provider.CreateProtector("Ecomads.Wildberries.Token.v1");

    public WbTokenClaims ReadClaims(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16000)
        {
            throw new ArgumentException("Укажите токен WB.", nameof(token));
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new ArgumentException("Неверный формат токена WB.", nameof(token));
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = json.RootElement;
            var sellerId = root.GetProperty("sid").GetString();
            var exp = root.GetProperty("exp").GetInt64();
            var tokenType = root.GetProperty("acc").GetInt32();
            var scopes = root.GetProperty("s").GetInt64();
            if (!Guid.TryParse(sellerId, out _) || exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                throw new ArgumentException("Токен WB истёк или не содержит ID продавца.", nameof(token));
            }
            if (root.TryGetProperty("t", out var test) && test.ValueKind == JsonValueKind.True)
            {
                throw new ArgumentException("Нужен токен для реального кабинета WB.", nameof(token));
            }
            if (tokenType != 1)
            {
                throw new ArgumentException("Создайте базовый токен WB для EcomAds.", nameof(token));
            }
            if ((scopes & (1L << 6)) == 0 || (scopes & (1L << 30)) == 0)
            {
                throw new ArgumentException("Токен WB должен иметь категорию «Продвижение» и доступ «Только чтение».", nameof(token));
            }

            return new WbTokenClaims(sellerId!, DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime,
                (scopes & (1L << 2)) != 0, (scopes & (1L << 5)) != 0);
        }
        catch (Exception error) when (error is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ArgumentException("Неверный формат токена WB.", nameof(token));
        }
    }

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}

namespace Ecomads.WebApplication.Auth;

/// <summary>
/// Настройки JWT для авторизации
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// Секретный ключ для подписи токена
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Издатель токена
    /// </summary>
    public string Issuer { get; set; } = string.Empty;
    
    /// <summary>
    /// Аудитория токена
    /// </summary>
    public string Audience { get; set; } = string.Empty;
    
    /// <summary>
    /// Время жизни токена в минутах
    /// </summary>
    public int ExpiryMinutes { get; set; } = 60;
}
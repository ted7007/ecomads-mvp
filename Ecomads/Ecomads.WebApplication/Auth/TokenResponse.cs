using System;

namespace Ecomads.WebApplication.Auth;

/// <summary>
/// Модель ответа с JWT токеном
/// </summary>
public class TokenResponse
{
    /// <summary>
    /// JWT токен
    /// </summary>
    public string Token { get; set; } = string.Empty;
    
    /// <summary>
    /// Время окончания действия токена в формате Unix timestamp
    /// </summary>
    public long ExpiresAt { get; set; }
    
    /// <summary>
    /// ID пользователя
    /// </summary>
    public Guid SellerId { get; set; }
    
    /// <summary>
    /// Имя пользователя
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Email пользователя
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
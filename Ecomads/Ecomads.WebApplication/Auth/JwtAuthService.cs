using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Ecomads.WebApplication.Auth;

public interface IJwtAuthService
{
    /// <summary>
    /// Генерирует JWT токен на основе данных пользователя
    /// </summary>
    TokenResponse GenerateToken(Seller seller);
    
    /// <summary>
    /// Аутентифицирует пользователя по email и паролю
    /// </summary>
    Task<Seller> Authenticate(string email, string password);
    
    /// <summary>
    /// Хеширует пароль для безопасного хранения
    /// </summary>
    string HashPassword(string password);
    
    /// <summary>
    /// Проверяет соответствие пароля хешу
    /// </summary>
    bool VerifyPassword(string password, string passwordHash);
}

public class JwtAuthService : IJwtAuthService
{
    private readonly JwtSettings _jwtSettings;
    private readonly EcomadsDbContext _dbContext;
    
    public JwtAuthService(
        IOptions<JwtSettings> jwtSettings,
        EcomadsDbContext dbContext)
    {
        _jwtSettings = jwtSettings.Value;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public TokenResponse GenerateToken(Seller seller)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes);
        var expirationTimeStamp = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();
        
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, seller.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, seller.Email),
            new Claim(JwtRegisteredClaimNames.Name, seller.Name),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64
            )
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );
        
        return new TokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expirationTimeStamp,
            SellerId = seller.Id,
            Name = seller.Name,
            Email = seller.Email
        };
    }

    /// <inheritdoc />
    public async Task<Seller> Authenticate(string email, string password)
    {
        var seller = await _dbContext.Sellers
            .FirstOrDefaultAsync(s => s.Email.ToLower() == email.ToLower());
        
        if (seller == null)
            return null;
        
        // Проверяем пароль
        if (!VerifyPassword(password, seller.PasswordHash))
            return null;
        
        // Обновляем время последнего входа
        seller.LastLoginAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        
        return seller;
    }

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        return BCryptNet.HashPassword(password, BCryptNet.GenerateSalt(12));
    }

    /// <inheritdoc />
    public bool VerifyPassword(string password, string passwordHash)
    {
        return BCryptNet.Verify(password, passwordHash);
    }
}
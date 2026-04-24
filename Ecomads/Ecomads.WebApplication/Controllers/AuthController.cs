using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Ecomads.WebApplication.Auth;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly EcomadsDbContext _dbContext;
    private readonly IJwtAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        EcomadsDbContext dbContext,
        IJwtAuthService authService,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Вход пользователя
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var seller = await _authService.Authenticate(request.Email, request.Password);
        
        if (seller == null)
        {
            _logger.LogWarning("Неудачная попытка входа для {Email}", request.Email);
            return Unauthorized(new { message = "Неверный email или пароль" });
        }
        
        _logger.LogInformation("Пользователь {Email} успешно вошел в систему", request.Email);
        
        var tokenResponse = _authService.GenerateToken(seller);
        return Ok(tokenResponse);
    }
    
    /// <summary>
    /// Регистрация нового пользователя (только для тестирования)
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        // Проверяем, что пользователь с таким email еще не существует
        if (await _dbContext.Sellers.AnyAsync(s => s.Email.ToLower() == request.Email.ToLower()))
        {
            return Conflict(new { message = "Пользователь с таким email уже существует" });
        }

        // Создаем нового пользователя
        var seller = new Seller
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            PasswordHash = _authService.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        var firstStore = new Store
        {
            Id = Guid.NewGuid(),
            Name = $"{request.Name} store",
            Description = null,
            Marketplace = null,
            ExternalId = null,
            ApiKey = null,
            CreatedAt = DateTime.UtcNow,
            LastSyncAt = DateTime.UtcNow,
            SellerId = seller.Id
        };

        _dbContext.Sellers.Add(seller);
        _dbContext.Stores.Add(firstStore);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Зарегистрирован новый пользователь {Email}", request.Email);
        
        // Сразу возвращаем токен для авторизации
        var tokenResponse = _authService.GenerateToken(seller);
        return Ok(tokenResponse);
    }
    
    /// <summary>
    /// Получение информации о текущем пользователе
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        // Получаем ID пользователя из клеймов
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var sellerId))
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }
        
        var seller = await _dbContext.Sellers
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Email,
                s.Phone,
                s.CreatedAt,
                s.LastLoginAt
            })
            .FirstOrDefaultAsync(s => s.Id == sellerId);
        
        if (seller == null)
        {
            return NotFound(new { message = "Пользователь не найден" });
        }
        
        return Ok(seller);
    }
}

/// <summary>
/// Модель запроса для входа
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// Email пользователя
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    
    /// <summary>
    /// Пароль
    /// </summary>
    [Required]
    public string Password { get; set; }
}

/// <summary>
/// Модель запроса для регистрации
/// </summary>
public class RegisterRequest
{
    /// <summary>
    /// Имя пользователя
    /// </summary>
    [Required]
    public string Name { get; set; }
    
    /// <summary>
    /// Email пользователя
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    
    /// <summary>
    /// Пароль
    /// </summary>
    [Required]
    [MinLength(6)]
    public string Password { get; set; }
}
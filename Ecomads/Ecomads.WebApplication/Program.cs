
using System;
using System.Text;
using Ecomads.WebApplication.Auth;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Middleware;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Wb;
using Ecomads.WebApplication.Services.Telegram;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Настройки JWT
var jwtSettingsSection = builder.Configuration.GetSection("JwtSettings");
builder.Services.Configure<JwtSettings>(jwtSettingsSection);

// Получаем настройки JWT из конфигурации
var jwtSettings = jwtSettingsSection.Get<JwtSettings>();
if (jwtSettings == null || string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
{
    throw new InvalidOperationException("JwtSettings:SecretKey must be configured.");
}

// Настраиваем JWT аутентификацию
var key = Encoding.ASCII.GetBytes(jwtSettings.SecretKey);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
});

// Добавляем сервис авторизации
builder.Services.AddScoped<IJwtAuthService, JwtAuthService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IWbTokenService, WbTokenService>();
builder.Services.AddScoped<WbFullStatsImporter>();
builder.Services.AddScoped<WbNormQueryImporter>();
builder.Services.AddScoped<WbJamImporter>();
builder.Services.AddScoped<WbSalesFunnelImporter>();
builder.Services.AddScoped<WbSyncPlanner>();
builder.Services.AddScoped<WbDataCoverageService>();
builder.Services.AddHostedService<WbSyncWorker>();
builder.Services.AddHostedService<WbAutoRefreshWorker>();
builder.Services.AddSingleton<ITelegramBotClient, TelegramBotClient>();
builder.Services.AddHostedService<TelegramUpdatesWorker>();
builder.Services.AddHttpClient<IWbPromotionClient, WbPromotionClient>(client =>
{
    client.BaseAddress = new Uri("https://advert-api.wildberries.ru");
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IWbJamClient, WbJamClient>(client =>
{
    client.BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru");
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IWbSalesFunnelClient, WbSalesFunnelClient>(client =>
{
    client.BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// Add services to the container.
builder.Services.AddDbContext<EcomadsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), o =>
    {
        o.EnableRetryOnFailure(5);
    }));

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
    dbContext.Database.Migrate();
}

app.UseForwardedHeaders();
app.UseStaticFiles();

// Добавляем middleware для аутентификации и авторизации
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<DemoAccessMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapFallbackToFile("/{*path:nonfile}", "index.html");

app.Run();

public partial class Program
{
}


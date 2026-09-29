
using System;
using System.Text;
using Ecomads.WebApplication.Auth;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Middleware;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Analytics;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RecommendationEngineOptions>(
    builder.Configuration.GetSection("RecommendationEngine"));

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
builder.Services.AddScoped<IProductAnalyticsService, ProductAnalyticsService>();
builder.Services.AddScoped<IStatisticsImportService, StatisticsImportService>();
builder.Services.AddScoped<ILlmUsageTrackingService, LlmUsageTrackingService>();

// Add services to the container.
builder.Services.AddDbContext<EcomadsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), o =>
    {
        o.EnableRetryOnFailure(5);
    }));

// Добавляем HttpClient и настраиваем HttpClientFactory
builder.Services.AddHttpClient("OpenAIClient", client =>
{
    // Базовая настройка HttpClient
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddSingleton<IStatisticsQueue, StatisticsQueue>();
builder.Services.AddSingleton<IRecommendationGoalMapper, RecommendationGoalMapper>();
builder.Services.AddSingleton<IRecommendationMetricCalculationService, MetricCalculationService>();
builder.Services.AddSingleton<IInsightGenerationService, InsightGenerationService>();
builder.Services.AddSingleton<IRecommendationPolicyService, RecommendationPolicyService>();
builder.Services.AddSingleton<IPriorityScoringService, PriorityScoringService>();
builder.Services.AddSingleton<IInsightSelectionService, InsightSelectionService>();
builder.Services.AddSingleton<IRecommendationPromptBuilder, RecommendationPromptBuilder>();
builder.Services.AddSingleton<IRecommendationInsightEntityMapper, RecommendationInsightEntityMapper>();
builder.Services.AddScoped<ILlmRecommendationTextService, LlmRecommendationTextService>();
builder.Services.AddScoped<IKeywordRecommendationOverlayService, KeywordRecommendationOverlayService>();
builder.Services.AddScoped<IInsightDecisionService, InsightDecisionService>();
// Изменяем регистрацию RecommendationService на Scoped
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddHostedService<StatisticsBackgroundService>();
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


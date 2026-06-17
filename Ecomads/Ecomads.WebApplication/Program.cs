
using System;
using System.Text;
using Ecomads.WebApplication.Auth;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
if (jwtSettings == null)
{
    // Создаем настройки по умолчанию, если их нет в конфигурации
    jwtSettings = new JwtSettings
    {
        SecretKey = "your_super_secret_key_at_least_32_bytes_long",
        Issuer = "ecomads",
        Audience = "ecomads_clients",
        ExpiryMinutes = 60 * 24 // 24 часа
    };
    
    // Сохраняем настройки в конфигурацию
    jwtSettingsSection.Bind(jwtSettings);
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


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
    if (app.Environment.IsDevelopment())
    {
        dbContext.Database.EnsureCreated();
    }
    else
    {
        dbContext.Database.Migrate();
    }
}

app.UseStaticFiles();

// Добавляем middleware для аутентификации и авторизации
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/app/"));
app.MapGet("/app", () => Results.Redirect("/app/"));
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

app.Run();


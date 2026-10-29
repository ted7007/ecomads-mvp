
using System;
using System.Text;
using Ecomads.WebApplication.Auth;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Middleware;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Wb;
using Ecomads.WebApplication.Services.Telegram;
using Ecomads.WebApplication.Services.Recommendations;
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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<WbCaptureHandler>();
builder.Services.AddSingleton<WbResponseJournal>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WbResponseJournal>());
builder.Services.AddScoped<WbQuality>();
builder.Services.AddSingleton<WbIngestionRollout>();
builder.Services.AddScoped<WbResponseProcessor>();
builder.Services.AddScoped<WbFullStatsImporter>();
builder.Services.AddScoped<WbNormQueryImporter>();
builder.Services.AddScoped<WbJamImporter>();
builder.Services.AddScoped<WbSalesFunnelImporter>();
builder.Services.AddScoped<WbProductOrdersRefresh>();
builder.Services.AddScoped<WbCostsImporter>();
builder.Services.AddScoped<WbSyncPlanner>();
builder.Services.AddScoped<WbScheduledRefreshService>();
builder.Services.AddScoped<WbReportReadinessService>();
builder.Services.AddScoped<WbDataCoverageService>();
builder.Services.AddOptions<RecommendationOptions>().Bind(builder.Configuration.GetSection("Wb:Recommendations"))
    .Validate(x => x.PriceScenario is >= 0 and < 1 && x.NoUnitsSpendFloor >= 0 && x.HighDrrMinimumUnits >= 1 && x.RetentionDays >= 1)
    .ValidateOnStart();
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<ResultsService>();
builder.Services.AddScoped<AdvertisingSettingsSync>();
builder.Services.AddScoped<OperationalSync>();
builder.Services.AddScoped<IWbOperationsClient, WbOperationsClient>();
builder.Services.AddHttpClient("wb-operations-analytics", client => { client.BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru"); client.Timeout = TimeSpan.FromMinutes(2); });
builder.Services.AddHttpClient("wb-operations-promotion", client => { client.BaseAddress = new Uri("https://advert-api.wildberries.ru"); client.Timeout = TimeSpan.FromMinutes(2); });
builder.Services.AddHostedService<RecommendationWorker>();
builder.Services.AddHostedService<WbSyncWorker>();
builder.Services.AddHostedService<WbProductOrdersWorker>();
builder.Services.AddHostedService<WbJournalRetentionWorker>();
builder.Services.AddHostedService<WbLegacyRecheckWorker>();
builder.Services.AddHostedService<WbSparseReplayWorker>();
builder.Services.AddHostedService<WbAutoRefreshWorker>();
builder.Services.AddSingleton<ITelegramBotClient, TelegramBotClient>();
builder.Services.AddScoped<TelegramDailySender>();
builder.Services.AddScoped<TelegramBotMenu>();
builder.Services.AddHostedService<TelegramUpdatesWorker>();
builder.Services.AddHostedService<TelegramDailySummaryWorker>();
builder.Services.AddHttpClient<IWbPromotionClient, WbPromotionClient>(client =>
{
    client.BaseAddress = new Uri("https://advert-api.wildberries.ru");
    client.Timeout = TimeSpan.FromMinutes(2);
}).AddHttpMessageHandler<WbCaptureHandler>();
builder.Services.AddHttpClient<IWbJamClient, WbJamClient>(client =>
{
    client.BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru");
    client.Timeout = TimeSpan.FromMinutes(2);
}).AddHttpMessageHandler<WbCaptureHandler>();
builder.Services.AddHttpClient<IWbSalesFunnelClient, WbSalesFunnelClient>(client =>
{
    client.BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru");
    client.Timeout = TimeSpan.FromMinutes(2);
}).AddHttpMessageHandler<WbCaptureHandler>();

// Add services to the container.
builder.Services.AddDbContext<EcomadsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), o =>
    {
        o.EnableRetryOnFailure(5);
    }));

builder.Services.AddControllers();
builder.Services.Configure<PilotApplicationOptions>(builder.Configuration.GetSection("PilotApplications"));
builder.Services.AddSingleton<PilotApplicationAccess>();
builder.Services.AddHostedService<PilotApplicationRetention>();
builder.Services.AddRateLimiter(PublicRequestLimits.Configure);
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 1;
    foreach (var network in (builder.Configuration["ReverseProxy:TrustedNetworks"] ?? "127.0.0.0/8,::1/128")
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
    dbContext.Database.Migrate();
}

app.UseForwardedHeaders();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/assets"))
            context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        else context.Context.Response.Headers.CacheControl = "no-cache";
    }
});
app.UseRouting();
app.UseRateLimiter();

// Добавляем middleware для аутентификации и авторизации
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<DemoAccessMiddleware>();

// Database errors on this public form must not log submitted contact parameters.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/pilot-applications") ||
        context.Request.Path.StartsWithSegments("/api/admin/pilot-applications"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path.Equals("/api/pilot-applications", StringComparison.OrdinalIgnoreCase) &&
            context.Request.ContentLength > 8192)
        {
            context.Response.StatusCode = 413;
            await context.Response.WriteAsJsonAsync(new { message = "Заявка слишком большая." });
            return;
        }
        try { await next(); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (BadHttpRequestException error)
        {
            context.Response.StatusCode = error.StatusCode;
            await context.Response.WriteAsJsonAsync(new { message = "Не удалось прочитать заявку." });
        }
        catch (Exception)
        {
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { message = "Не удалось сохранить заявку. Попробуйте ещё раз." });
        }
    }
    else await next();
});

app.MapControllers();
app.MapHealthChecks("/health");
app.MapFallbackToFile("/{*path:nonfile}", "index.html");

app.Run();

public partial class Program
{
}


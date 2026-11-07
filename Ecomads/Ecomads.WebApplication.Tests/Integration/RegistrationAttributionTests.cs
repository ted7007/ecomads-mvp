using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ecomads.WebApplication.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class RegistrationAttributionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RegistrationSavesSourceAndLoginCannotReplaceIt()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        using var factory = new Factory(db.Database.GetConnectionString()!);
        using var client = factory.CreateClient();
        var capturedAt = DateTime.UtcNow.AddDays(-2);
        capturedAt = capturedAt.AddTicks(-(capturedAt.Ticks % TimeSpan.TicksPerMillisecond));
        var attribution = new { utm_source = "Campaign_A.1-2", captured_at_utc = capturedAt, other = "ignored" };
        using var response = await client.PostAsJsonAsync("/api/auth/register", Request("source@example.test", attribution));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seller = await db.Sellers.SingleAsync();
        Assert.Equal(attribution.utm_source, seller.UtmSource);
        Assert.Equal(capturedAt, seller.UtmCapturedAtUtc);
        Assert.Equal(seller.Id, (await db.Stores.SingleAsync()).SellerId);

        using var login = await client.PostAsJsonAsync("/api/auth/login?utm_source=B", new
        {
            email = seller.Email, password = "password123",
            attribution = new { utm_source = "B", captured_at_utc = DateTime.UtcNow }
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await db.Entry(seller).ReloadAsync();
        Assert.Equal(attribution.utm_source, seller.UtmSource);
        Assert.Equal(capturedAt, seller.UtmCapturedAtUtc);
        using var duplicate = await client.PostAsJsonAsync("/api/auth/register", Request(seller.Email,
            new { utm_source = "B", captured_at_utc = DateTime.UtcNow }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await db.Entry(seller).ReloadAsync();
        Assert.Equal(attribution.utm_source, seller.UtmSource);
    }

    [Fact]
    public async Task MissingAndMalformedAttributionNeverRejectOtherwiseValidRegistration()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        using var factory = new Factory(db.Database.GetConnectionString()!);
        using var client = factory.CreateClient();
        var now = DateTime.UtcNow;
        object?[] invalid = [null, "wrong type", 5, true, new[] { "A" },
            new { utm_source = "A" }, new { captured_at_utc = now },
            new { utm_source = 42, captured_at_utc = now },
            new { utm_source = "A", captured_at_utc = false },
            new { utm_source = "A", captured_at_utc = "invalid date" },
            new { utm_source = "A", captured_at_utc = now.AddDays(-31) },
            new { utm_source = "A", captured_at_utc = now.AddDays(1) },
            new { utm_source = "", captured_at_utc = now },
            new { utm_source = "invalid source", captured_at_utc = now },
            new { utm_source = "source\n", captured_at_utc = now },
            new { utm_source = new string('a', 129), captured_at_utc = now },
            JsonDocument.Parse("""{"utm_source":"A","utm_source":"B","captured_at_utc":"2026-11-07T00:00:00Z"}""").RootElement];
        for (var i = 0; i < invalid.Length; i++)
        {
            var email = $"invalid-{i}@example.test";
            var request = Request(email, invalid[i]);
            if (i == 0) request.Remove("attribution");
            using var response = await client.PostAsJsonAsync("/api/auth/register", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var seller = await db.Sellers.SingleAsync(x => x.Email == email);
            Assert.Null(seller.UtmSource);
            Assert.Null(seller.UtmCapturedAtUtc);
        }

        using var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "invalid-0@example.test", password = "password123",
            attribution = new { utm_source = "B", captured_at_utc = now }
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Null((await db.Sellers.SingleAsync(x => x.Email == "invalid-0@example.test")).UtmSource);
    }

    [Fact]
    public void ExpiryUsesTheOriginalTimestampAndExactThirtyDayBoundary()
    {
        var now = new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);
        JsonElement Value(DateTime capturedAt) => JsonSerializer.SerializeToElement(new
            { utm_source = "A", captured_at_utc = capturedAt });
        Assert.NotNull(RegistrationAttribution.Parse(Value(now), now));
        Assert.NotNull(RegistrationAttribution.Parse(Value(now.AddDays(-30).AddTicks(1)), now));
        Assert.Null(RegistrationAttribution.Parse(Value(now.AddDays(-30)), now));
        Assert.Null(RegistrationAttribution.Parse(Value(now.AddTicks(1)), now));
    }

    [Fact]
    public async Task MigrationPreservesExistingAccountsWithNullAttribution()
    {
        var connection = await postgres.CreateDatabaseConnectionStringAsync();
        await using var db = postgres.CreateDbContext(connection);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261105044443_AddDemoRegistrationSegments");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sellers (id, name, email, password_hash, created_at)
            VALUES ({id}, 'Existing user', 'existing@example.test', 'unused', {DateTime.UtcNow})
            """);
        await db.Database.MigrateAsync();
        var seller = await db.Sellers.SingleAsync();
        Assert.Equal(id, seller.Id);
        Assert.Null(seller.UtmSource);
        Assert.Null(seller.UtmCapturedAtUtc);
        await migrator.MigrateAsync("20261105044443_AddDemoRegistrationSegments");
        Assert.Equal(1, await db.Sellers.CountAsync());
    }

    private static Dictionary<string, object?> Request(string email, object? attribution) => new()
    {
        ["name"] = "Registration test", ["email"] = email, ["password"] = "password123",
        ["attribution"] = attribution
    };

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["JwtSettings:SecretKey"] = "registration-tests-only-secret-at-least-32-characters"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IStartupFilter, ClientAddressFilter>();
            });
        }
    }

    private sealed class ClientAddressFilter : IStartupFilter
    {
        private int requestNumber;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, handler) =>
            {
                // Each test request represents a different client, preserving production rate limits.
                context.Connection.RemoteIpAddress = IPAddress.Parse($"203.0.113.{Interlocked.Increment(ref requestNumber)}");
                return handler(context);
            });
            next(app);
        };
    }
}

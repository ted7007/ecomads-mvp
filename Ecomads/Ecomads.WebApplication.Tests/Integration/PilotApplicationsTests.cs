using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Middleware;
using Ecomads.WebApplication.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class PilotApplicationsTests(PostgresFixture postgres)
{
    private async Task<string> PreparedConnection()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        return db.Database.GetConnectionString()!;
    }

    private static readonly Guid AdminId = Guid.Parse("4be3d30f-e663-4ef4-970a-59e3dc4eb617");
    private static object Request(Guid? id = null, string website = "", bool consent = true) => new
    { submissionId = id ?? Guid.NewGuid(), name = "Тест пилота", contact = "@pilot_test", contactConsent = consent, website };

    [Fact]
    public async Task SubmissionIsDurableAndConcurrentRetryIsIdempotent()
    {
        var connection = await PreparedConnection();
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        var request = Request();
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/pilot-applications", request),
            client.PostAsJsonAsync("/api/pilot-applications", request));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        await using var db = postgres.CreateDbContext(connection);
        var item = await db.PilotApplications.SingleAsync();
        Assert.Equal("@pilot_test", item.Contact);
        Assert.Equal("new", item.Status);
        Assert.Equal(item.CreatedAtUtc, item.ContactConsentAtUtc);
        Assert.Empty(await db.Sellers.ToListAsync());
    }

    [Fact]
    public async Task ConsentValidationHoneypotAndContentTypeAreEnforced()
    {
        var connection = await PreparedConnection();
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pilot-applications", Request(consent: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pilot-applications", new
        { submissionId = Guid.NewGuid(), name = "  ", contact = "bad", contactConsent = true, stores = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/pilot-applications", Request(website: "https://spam.invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/pilot-applications", new StringContent("name=test"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/pilot-applications", new StringContent(new string('x', 9000), Encoding.UTF8, "application/json"))).StatusCode);
        await using var db = postgres.CreateDbContext(connection);
        Assert.Empty(await db.PilotApplications.ToListAsync());
    }

    [Fact]
    public async Task SpoofedForwardedAddressesDoNotBypassLimits()
    {
        var connection = await PreparedConnection();
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        for (var i = 0; i < 6; i++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/pilot-applications") { Content = JsonContent.Create(Request(website: "bot")) };
            message.Headers.Add("X-Forwarded-For", $"198.51.100.{i + 1}");
            using var response = await client.SendAsync(message);
            Assert.Equal(i == 5 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, response.StatusCode);
            if (i == 5) Assert.NotNull(response.Headers.RetryAfter);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task GlobalAndConcurrencyLimitsApplyAcrossAddresses()
    {
        var options = new RateLimiterOptions();
        PublicRequestLimits.Configure(options);
        using var limiter = options.GlobalLimiter!;
        DefaultHttpContext Context(int i)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST"; context.Request.Path = "/api/pilot-applications";
            context.Connection.RemoteIpAddress = IPAddress.Parse($"198.51.100.{i}");
            return context;
        }
        var leases = new List<System.Threading.RateLimiting.RateLimitLease>();
        for (var i = 1; i <= 4; i++) { var lease = await limiter.AcquireAsync(Context(i)); Assert.True(lease.IsAcquired); leases.Add(lease); }
        using (var rejected = await limiter.AcquireAsync(Context(5))) Assert.False(rejected.IsAcquired);
        foreach (var lease in leases) lease.Dispose();
        // 5 attempts have consumed the global minute budget (including concurrency rejection).
        for (var i = 6; i <= 60; i++) { using var lease = await limiter.AcquireAsync(Context(i)); Assert.True(lease.IsAcquired); }
        using var globalRejected = await limiter.AcquireAsync(Context(61));
        Assert.False(globalRejected.IsAcquired);
    }

    [Fact]
    public async Task AdminEndpointsRejectGuestsAndRegularUsersAndAllowConfiguredAccount()
    {
        var connection = await PreparedConnection();
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/pilot-applications", Request());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/pilot-applications")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/pilot-applications")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-UserId"); client.DefaultRequestHeaders.Add("X-Test-UserId", AdminId.ToString());
        using var list = await client.GetAsync("/api/admin/pilot-applications");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl!.NoStore);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/admin/pilot-applications/{id}", new { status = "contacted", note = "Договорились о подключении" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/admin/pilot-applications/{id}", new { status = "admin", note = "" })).StatusCode);
        using var filtered = await client.GetAsync("/api/admin/pilot-applications?status=contacted");
        Assert.Equal(1, (await filtered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32());
        await using var db = postgres.CreateDbContext(connection);
        Assert.Equal("contacted", (await db.PilotApplications.SingleAsync()).Status);
        var seller = TestData.CreateRegularSeller(); seller.Id = AdminId; db.Sellers.Add(seller); await db.SaveChangesAsync();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("canManagePilotApplications").GetBoolean());
    }

    [Fact]
    public async Task RetentionDeletesOnlyRecordsAtLeast180DaysOld()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var now = DateTime.UtcNow;
        db.PilotApplications.AddRange(
            new PilotApplication { SubmissionId = Guid.NewGuid(), Name = "Old", Contact = "@oldtest", CreatedAtUtc = now.AddDays(-180), UpdatedAtUtc = now, ContactConsentAtUtc = now },
            new PilotApplication { SubmissionId = Guid.NewGuid(), Name = "Recent", Contact = "@recent", CreatedAtUtc = now.AddDays(-179), UpdatedAtUtc = now, ContactConsentAtUtc = now });
        await db.SaveChangesAsync();
        Assert.Equal(1, await PilotApplicationRetention.DeleteExpiredAsync(db, now, default));
        Assert.Equal("Recent", (await db.PilotApplications.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task DatabaseFailureReturnsRetryableErrorWithoutFalseSuccess()
    {
        var connection = await PreparedConnection();
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        await using var db = postgres.CreateDbContext(connection);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE pilot_applications RENAME TO pilot_applications_unavailable");
        using var response = await client.PostAsJsonAsync("/api/pilot-applications", Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("accepted", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginAndRegistrationHaveIndependentRateLimits()
    {
        var connection = await PreparedConnection();
        using var factory = new Factory(connection);
        using var client = factory.CreateClient();
        for (var i = 0; i < 11; i++)
            Assert.Equal(i == 10 ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
        for (var i = 0; i < 4; i++)
            Assert.Equal(i == 3 ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/auth/register", new { })).StatusCode);
    }

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["JwtSettings:SecretKey"] = "pilot-tests-only-secret-key-which-is-at-least-32-bytes",
                ["PilotApplications:AdminSellerIds"] = AdminId.ToString(),
                ["ReverseProxy:TrustedNetworks"] = "10.254.0.0/16"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter, RemoteAddressFilter>();
                services.RemoveAll<IHostedService>();
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "Test"; options.DefaultChallengeScheme = "Test"; })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-UserId", out var value)) return Task.FromResult(AuthenticateResult.NoResult());
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, value.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name)));
        }
    }

    // TestServer normally has no transport peer; use an actual untrusted peer address.
    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use((context, handler) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
                return handler(context);
            });
            next(app);
        };
    }
}

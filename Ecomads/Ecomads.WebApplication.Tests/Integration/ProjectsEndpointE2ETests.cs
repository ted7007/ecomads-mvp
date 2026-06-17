using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class ProjectsEndpointE2ETests
{
    private const string TestAuthScheme = "Test";
    private const string TestUserIdHeader = "X-Test-UserId";
    private readonly PostgresFixture _postgres;

    public ProjectsEndpointE2ETests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task GetProjects_WithDateOnlyQuery_DoesNotSendUnspecifiedDateTimeToPostgres()
    {
        var connectionString = await _postgres.CreateDatabaseConnectionStringAsync();

        using var factory = new ProjectsEndpointFactory(connectionString);
        using var client = factory.CreateClient();

        var seller = TestData.CreateActiveDemoSeller();
        var store = new Store
        {
            Id = Guid.NewGuid(),
            Name = "E2E Store",
            SellerId = seller.Id
        };
        var campaign = new Compaign
        {
            Id = Guid.NewGuid(),
            Name = "June Campaign",
            Number = "SKU-001",
            StoreId = store.Id
        };

        await using (var dbContext = _postgres.CreateDbContext(connectionString))
        {
            dbContext.Sellers.Add(seller);
            dbContext.Stores.Add(store);
            dbContext.Compaigns.Add(campaign);
            dbContext.CompaignStatistics.Add(new CompaignStatistics
            {
                CompaignId = campaign.Id,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
                Type = CompaignStatisticsType.General,
                Spend = 100,
                Revenue = 500,
                Clicks = 20,
                Ctr = 10,
                Drr = 20
            });

            await dbContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Add(TestUserIdHeader, seller.Id.ToString());

        using var response = await client.GetAsync(
            "/api/projects?startDate=2026-06-01&endDate=2026-06-07&source=dashboard");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Status: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");

        using var document = JsonDocument.Parse(body);
        var project = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(campaign.Id, project.GetProperty("id").GetGuid());
        Assert.Equal(campaign.Name, project.GetProperty("name").GetString());
        Assert.Equal(100, project.GetProperty("kpi").GetProperty("spend").GetDouble());
    }

    private sealed class ProjectsEndpointFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public ProjectsEndpointFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _connectionString,
                    ["OpenAI:ApiKey"] = "test-api-key",
                    ["OpenAI:BaseUrl"] = "https://example.test",
                    ["OpenAI:Model"] = "test-model"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthScheme;
                        options.DefaultChallengeScheme = TestAuthScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthScheme, _ => { });
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(TestUserIdHeader, out var userIdHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var userId = userIdHeader.ToString();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

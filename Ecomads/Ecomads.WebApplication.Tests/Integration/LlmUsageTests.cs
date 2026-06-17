using System.Net;
using System.Text;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services.Analytics;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class LlmUsageTests
{
    private readonly PostgresFixture _postgres;

    public LlmUsageTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task LlmRecommendationTextServiceSavesUsageWhenResponseContainsUsage()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var handler = new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                  "id": "chatcmpl-test",
                  "model": "gpt-4o-mini",
                  "choices": [
                    {
                      "message": {
                        "content": "Готовая рекомендация."
                      }
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 10,
                    "completion_tokens": 5,
                    "total_tokens": 15,
                    "bothub": {
                      "caps": 0.123456
                    }
                  }
                }
                """)
        });
        var service = CreateLlmTextService(dbContext, handler);

        var result = await service.GenerateTextAsync(
            "prompt",
            new LlmRecommendationTextContext(
                seller.Id,
                CampaignId: null,
                KeywordId: null,
                LlmOperations.GenerateCampaignRecommendations,
                SelectedInsightsCount: 3));

        Assert.False(result.GeneratedWithoutLlm);
        Assert.NotNull(result.LlmUsageId);
        Assert.Contains("\"include_usage\":true", handler.RequestBodies.Single());

        var usageEvent = await dbContext.LlmUsageEvents.SingleAsync();
        Assert.True(usageEvent.IsSuccess);
        Assert.Equal(10, usageEvent.PromptTokens);
        Assert.Equal(5, usageEvent.CompletionTokens);
        Assert.Equal(15, usageEvent.TotalTokens);
        Assert.Equal(0.123456m, usageEvent.BothubCaps);
    }

    [Fact]
    public async Task LlmRecommendationTextServiceSavesUsageEventWithNullTokensWhenUsageIsMissing()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var handler = new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "Готовая рекомендация без usage."
                      }
                    }
                  ]
                }
                """)
        });
        var service = CreateLlmTextService(dbContext, handler);

        var result = await service.GenerateTextAsync(
            "prompt",
            new LlmRecommendationTextContext(
                seller.Id,
                CampaignId: null,
                KeywordId: null,
                LlmOperations.GenerateCampaignRecommendations,
                SelectedInsightsCount: 1));

        Assert.False(result.GeneratedWithoutLlm);

        var usageEvent = await dbContext.LlmUsageEvents.SingleAsync();
        Assert.True(usageEvent.IsSuccess);
        Assert.Null(usageEvent.PromptTokens);
        Assert.Null(usageEvent.CompletionTokens);
        Assert.Null(usageEvent.TotalTokens);
        Assert.Null(usageEvent.BothubCaps);
    }

    [Fact]
    public async Task LlmRecommendationTextServiceSavesFailedUsageEventWhenRequestFails()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var handler = new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "server error"
        });
        var service = CreateLlmTextService(dbContext, handler);

        var result = await service.GenerateTextAsync(
            "prompt",
            new LlmRecommendationTextContext(
                seller.Id,
                CampaignId: null,
                KeywordId: null,
                LlmOperations.GenerateCampaignRecommendations,
                SelectedInsightsCount: 1));

        Assert.True(result.GeneratedWithoutLlm);
        Assert.NotNull(result.LlmUsageId);

        var usageEvent = await dbContext.LlmUsageEvents.SingleAsync();
        Assert.False(usageEvent.IsSuccess);
        Assert.Equal(500, usageEvent.HttpStatusCode);
        Assert.Equal("http_error", usageEvent.ErrorCode);
    }

    [Fact]
    public async Task LlmUsageTrackingServiceSavesSuccessAndFailureEvents()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var service = new LlmUsageTrackingService(dbContext, NullLogger<LlmUsageTrackingService>.Instance);

        var successId = await service.TrackSuccessAsync(new LlmUsageSuccessDto
        {
            Provider = "bothub",
            Model = "gpt-4o-mini",
            OperationName = LlmOperations.GenerateCampaignRecommendations,
            PromptTokens = 1,
            CompletionTokens = 2,
            TotalTokens = 3,
            BothubCaps = 0.1m,
            DurationMs = 42
        });
        var failureId = await service.TrackFailureAsync(new LlmUsageFailureDto
        {
            Provider = "bothub",
            Model = "gpt-4o-mini",
            OperationName = LlmOperations.GenerateCampaignRecommendations,
            HttpStatusCode = 429,
            ErrorCode = "rate_limit",
            ErrorMessage = "Rate limit",
            DurationMs = 43
        });

        Assert.NotNull(successId);
        Assert.NotNull(failureId);
        Assert.Equal(2, await dbContext.LlmUsageEvents.CountAsync());
        Assert.True(await dbContext.LlmUsageEvents.AnyAsync(item => item.Id == successId && item.IsSuccess));
        Assert.True(await dbContext.LlmUsageEvents.AnyAsync(item => item.Id == failureId && !item.IsSuccess));
    }

    private static LlmRecommendationTextService CreateLlmTextService(
        Ecomads.WebApplication.Data.EcomadsDbContext dbContext,
        QueueHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key",
                ["OpenAI:BaseUrl"] = "https://openai.bothub.chat/v1/chat/completions",
                ["OpenAI:Model"] = "gpt-4o-mini",
                ["OpenAI:IncludeUsage"] = "true"
            })
            .Build();
        var usageTrackingService = new LlmUsageTrackingService(dbContext, NullLogger<LlmUsageTrackingService>.Instance);

        return new LlmRecommendationTextService(
            new StubHttpClientFactory(new HttpClient(handler)),
            configuration,
            usageTrackingService,
            NullLogger<LlmRecommendationTextService>.Instance);
    }

    private static StringContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public StubHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name)
        {
            return _httpClient;
        }
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _responses.Dequeue();
        }
    }
}

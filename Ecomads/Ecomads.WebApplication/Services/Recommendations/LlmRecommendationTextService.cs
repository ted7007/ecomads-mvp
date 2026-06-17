using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services.Analytics;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record LlmRecommendationTextResult(
    string Text,
    bool GeneratedWithoutLlm,
    string? Error = null,
    Guid? LlmUsageId = null);

public sealed record LlmRecommendationTextContext(
    Guid? UserId,
    Guid? CampaignId,
    Guid? KeywordId,
    string OperationName,
    int SelectedInsightsCount);

public interface ILlmRecommendationTextService
{
    Task<LlmRecommendationTextResult> GenerateTextAsync(
        string prompt,
        LlmRecommendationTextContext context,
        CancellationToken cancellationToken = default);
}

public sealed class LlmRecommendationTextService : ILlmRecommendationTextService
{
    private const double Temperature = 0.3;

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILlmUsageTrackingService _usageTrackingService;
    private readonly ILogger<LlmRecommendationTextService> _logger;

    public LlmRecommendationTextService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILlmUsageTrackingService usageTrackingService,
        ILogger<LlmRecommendationTextService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _usageTrackingService = usageTrackingService;
        _logger = logger;
    }

    public async Task<LlmRecommendationTextResult> GenerateTextAsync(
        string prompt,
        LlmRecommendationTextContext context,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"];
        var baseUrl = _configuration["OpenAI:BaseUrl"];
        var model = _configuration["OpenAI:Model"];
        var provider = ResolveProvider(baseUrl);
        var modelName = string.IsNullOrWhiteSpace(model) ? "unknown" : model;
        var includeUsage = _configuration.GetValue("OpenAI:IncludeUsage", true);
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(model))
        {
            stopwatch.Stop();

            _logger.LogError(
                "LLM request skipped because OpenAI configuration is incomplete. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}",
                provider,
                modelName,
                context.OperationName,
                context.UserId,
                context.CampaignId);

            var llmUsageId = await _usageTrackingService.TrackFailureAsync(new LlmUsageFailureDto
            {
                UserId = context.UserId,
                CampaignId = context.CampaignId,
                KeywordId = context.KeywordId,
                Provider = provider,
                Model = modelName,
                OperationName = context.OperationName,
                ErrorCode = "configuration_incomplete",
                ErrorMessage = "OpenAI configuration is incomplete.",
                DurationMs = stopwatch.ElapsedMilliseconds,
                RequestMetadata = BuildRequestMetadata(prompt, context, includeUsage, retriedWithoutIncludeUsage: false),
                ResponseMetadata = new { failureStage = "configuration" }
            });

            return new LlmRecommendationTextResult(
                string.Empty,
                true,
                "OpenAI configuration is incomplete.",
                llmUsageId);
        }

        HttpResponseMessage? response = null;
        try
        {
            var httpClient = _httpClientFactory.CreateClient("OpenAIClient");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            _logger.LogInformation(
                "Starting LLM request. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}, IncludeUsage: {IncludeUsage}",
                provider,
                model,
                context.OperationName,
                context.UserId,
                context.CampaignId,
                includeUsage);

            response = await SendRequestAsync(httpClient, baseUrl, model, prompt, includeUsage, cancellationToken);
            var retriedWithoutIncludeUsage = false;
            if (includeUsage && response.StatusCode == HttpStatusCode.BadRequest)
            {
                response.Dispose();
                retriedWithoutIncludeUsage = true;

                _logger.LogWarning(
                    "LLM request returned BadRequest with include_usage enabled. Retrying without include_usage. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}",
                    provider,
                    model,
                    context.OperationName,
                    context.UserId,
                    context.CampaignId);

                response = await SendRequestAsync(httpClient, baseUrl, model, prompt, includeUsage: false, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();

                var error = $"LLM request failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                _logger.LogError(
                    "LLM request failed. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}, StatusCode: {StatusCode}",
                    provider,
                    model,
                    context.OperationName,
                    context.UserId,
                    context.CampaignId,
                    (int)response.StatusCode);

                var llmUsageId = await _usageTrackingService.TrackFailureAsync(new LlmUsageFailureDto
                {
                    UserId = context.UserId,
                    CampaignId = context.CampaignId,
                    KeywordId = context.KeywordId,
                    Provider = provider,
                    Model = model,
                    OperationName = context.OperationName,
                    HttpStatusCode = (int)response.StatusCode,
                    ErrorCode = "http_error",
                    ErrorMessage = error,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    RequestMetadata = BuildRequestMetadata(prompt, context, includeUsage, retriedWithoutIncludeUsage),
                    ResponseMetadata = BuildFailureResponseMetadata(response, retriedWithoutIncludeUsage)
                });

                return new LlmRecommendationTextResult(string.Empty, true, error, llmUsageId);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var llmResponse = JsonSerializer.Deserialize<LlmResponse>(responseJson, ResponseJsonOptions);
            var resultText = llmResponse?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(resultText))
            {
                stopwatch.Stop();

                _logger.LogError(
                    "LLM response is empty. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}",
                    provider,
                    model,
                    context.OperationName,
                    context.UserId,
                    context.CampaignId);

                var llmUsageId = await _usageTrackingService.TrackFailureAsync(new LlmUsageFailureDto
                {
                    UserId = context.UserId,
                    CampaignId = context.CampaignId,
                    KeywordId = context.KeywordId,
                    Provider = provider,
                    Model = model,
                    OperationName = context.OperationName,
                    HttpStatusCode = (int)response.StatusCode,
                    ErrorCode = "empty_response",
                    ErrorMessage = "LLM response is empty.",
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    RequestMetadata = BuildRequestMetadata(prompt, context, includeUsage, retriedWithoutIncludeUsage),
                    ResponseMetadata = BuildResponseMetadata(llmResponse, retriedWithoutIncludeUsage)
                });

                return new LlmRecommendationTextResult(
                    string.Empty,
                    true,
                    "LLM response is empty.",
                    llmUsageId);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "LLM request succeeded. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}, PromptTokens: {PromptTokens}, CompletionTokens: {CompletionTokens}, TotalTokens: {TotalTokens}, BothubCaps: {BothubCaps}, DurationMs: {DurationMs}",
                provider,
                model,
                context.OperationName,
                context.UserId,
                context.CampaignId,
                llmResponse?.Usage?.PromptTokens,
                llmResponse?.Usage?.CompletionTokens,
                llmResponse?.Usage?.TotalTokens,
                llmResponse?.Usage?.Bothub?.Caps,
                stopwatch.ElapsedMilliseconds);

            var successUsageId = await _usageTrackingService.TrackSuccessAsync(new LlmUsageSuccessDto
            {
                UserId = context.UserId,
                CampaignId = context.CampaignId,
                KeywordId = context.KeywordId,
                Provider = provider,
                Model = model,
                OperationName = context.OperationName,
                PromptTokens = llmResponse?.Usage?.PromptTokens,
                CompletionTokens = llmResponse?.Usage?.CompletionTokens,
                TotalTokens = llmResponse?.Usage?.TotalTokens,
                BothubCaps = llmResponse?.Usage?.Bothub?.Caps,
                HttpStatusCode = (int)response.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                RequestMetadata = BuildRequestMetadata(prompt, context, includeUsage, retriedWithoutIncludeUsage),
                ResponseMetadata = BuildResponseMetadata(llmResponse, retriedWithoutIncludeUsage)
            });

            return new LlmRecommendationTextResult(resultText, false, LlmUsageId: successUsageId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "LLM request failed with exception. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}",
                provider,
                modelName,
                context.OperationName,
                context.UserId,
                context.CampaignId);

            var llmUsageId = await _usageTrackingService.TrackFailureAsync(new LlmUsageFailureDto
            {
                UserId = context.UserId,
                CampaignId = context.CampaignId,
                KeywordId = context.KeywordId,
                Provider = provider,
                Model = modelName,
                OperationName = context.OperationName,
                ErrorCode = "exception",
                ErrorMessage = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
                RequestMetadata = BuildRequestMetadata(prompt, context, includeUsage, retriedWithoutIncludeUsage: false),
                ResponseMetadata = new { failureStage = "exception", exceptionType = ex.GetType().Name }
            });

            return new LlmRecommendationTextResult(string.Empty, true, ex.Message, llmUsageId);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(
        HttpClient httpClient,
        string baseUrl,
        string model,
        string prompt,
        bool includeUsage,
        CancellationToken cancellationToken)
    {
        var json = BuildRequestJson(model, prompt, includeUsage);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        return await httpClient.PostAsync(baseUrl, content, cancellationToken);
    }

    private static string BuildRequestJson(string model, string prompt, bool includeUsage)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new
                {
                    role = "system",
                    content = "Ты эксперт по рекламе Wildberries. Объясняй только backend-provided insights."
                },
                new { role = "user", content = prompt }
            },
            ["temperature"] = Temperature
        };

        if (includeUsage)
        {
            requestBody["bothub"] = new
            {
                include_usage = true
            };
        }

        return JsonSerializer.Serialize(requestBody, RequestJsonOptions);
    }

    private static object BuildRequestMetadata(
        string prompt,
        LlmRecommendationTextContext context,
        bool includeUsageRequested,
        bool retriedWithoutIncludeUsage)
    {
        return new
        {
            temperature = Temperature,
            messagesCount = 2,
            promptLength = prompt.Length,
            selectedInsightsCount = context.SelectedInsightsCount,
            includeUsageRequested,
            retriedWithoutIncludeUsage
        };
    }

    private static object BuildResponseMetadata(LlmResponse? response, bool retriedWithoutIncludeUsage)
    {
        return new
        {
            responseId = response?.Id,
            responseModel = response?.Model,
            responseCreated = response?.Created,
            choicesCount = response?.Choices?.Count,
            hasUsage = response?.Usage != null,
            hasBothubUsage = response?.Usage?.Bothub != null,
            retriedWithoutIncludeUsage
        };
    }

    private static object BuildFailureResponseMetadata(HttpResponseMessage response, bool retriedWithoutIncludeUsage)
    {
        return new
        {
            httpStatusCode = (int)response.StatusCode,
            reasonPhrase = response.ReasonPhrase,
            retriedWithoutIncludeUsage
        };
    }

    private static string ResolveProvider(string? baseUrl)
    {
        return baseUrl?.Contains("bothub", StringComparison.OrdinalIgnoreCase) == true
            ? "bothub"
            : "openai-compatible";
    }
}

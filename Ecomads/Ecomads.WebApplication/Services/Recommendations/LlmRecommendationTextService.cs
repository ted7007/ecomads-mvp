using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Models;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record LlmRecommendationTextResult(
    string Text,
    bool GeneratedWithoutLlm,
    string? Error = null);

public interface ILlmRecommendationTextService
{
    Task<LlmRecommendationTextResult> GenerateTextAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}

public sealed class LlmRecommendationTextService : ILlmRecommendationTextService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmRecommendationTextService> _logger;

    public LlmRecommendationTextService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<LlmRecommendationTextService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LlmRecommendationTextResult> GenerateTextAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"];
        var baseUrl = _configuration["OpenAI:BaseUrl"];
        var model = _configuration["OpenAI:Model"];

        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(model))
        {
            return new LlmRecommendationTextResult(
                string.Empty,
                true,
                "OpenAI configuration is incomplete.");
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient("OpenAIClient");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Ты эксперт по рекламе Wildberries. Объясняй только backend-provided insights."
                    },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(baseUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"LLM request failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                _logger.LogError("{Error}", error);

                return new LlmRecommendationTextResult(string.Empty, true, error);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var llmResponse = JsonSerializer.Deserialize<LlmResponse>(responseJson);
            var resultText = llmResponse?.choices?.FirstOrDefault()?.message?.content;

            if (string.IsNullOrWhiteSpace(resultText))
            {
                return new LlmRecommendationTextResult(
                    string.Empty,
                    true,
                    "LLM response is empty.");
            }

            _logger.LogInformation("Получен LLM-текст рекомендации длиной {Length} символов", resultText.Length);
            return new LlmRecommendationTextResult(resultText, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации текста рекомендации через LLM");
            return new LlmRecommendationTextResult(string.Empty, true, ex.Message);
        }
    }
}

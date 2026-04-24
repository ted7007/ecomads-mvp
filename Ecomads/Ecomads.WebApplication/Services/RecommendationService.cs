using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using System.Collections.Generic;

namespace Ecomads.WebApplication.Services;

public interface IRecommendationService
{
    Task<Recommendation?> GenerateRecommendationAsync(Guid campaignId, string goal);
}

public class RecommendationService : IRecommendationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecommendationService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    public RecommendationService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<RecommendationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        _apiKey = configuration["OpenAI:ApiKey"] ?? throw new ArgumentNullException("OpenAI:ApiKey is missing");
        _baseUrl = configuration["OpenAI:BaseUrl"] ?? throw new ArgumentNullException("OpenAI:BaseUrl is missing");
        _model = configuration["OpenAI:Model"] ?? throw new ArgumentNullException("OpenAI:Model is missing");
    }

    public async Task<Recommendation?> GenerateRecommendationAsync(Guid campaignId, string goal)
    {
        // Создаем scoped контекст для работы с базой данных
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        
        // 1. Получение данных из БД (как в консольном приложении)
        var campaign = await dbContext.Compaigns.FindAsync(campaignId);
        if (campaign == null) return null;

        var stats = await dbContext.CompaignStatistics
            .FirstOrDefaultAsync(s => s.CompaignId == campaignId && s.Type == (CompaignStatisticsType)0);

        var topKeywords = await dbContext.KeywordStatistics
            .Where(k => k.CompaignId == campaignId)
            .OrderByDescending(k => k.Revenue)
            .Take(5)
            .ToListAsync();

        var worstKeywords = await dbContext.KeywordStatistics
            .Where(k => k.CompaignId == campaignId && k.Orders == 0 && k.Spend > 0)
            .OrderByDescending(k => k.Spend)
            .Take(5)
            .ToListAsync();

        var dto = new CampaignAnalyticsDto
        {
            Name = campaign.Name,
            Spend = Convert.ToDouble(stats?.Spend ?? 0),
            Revenue = Convert.ToDouble(stats?.Revenue ?? 0),
            Drr = stats?.Drr ?? 0,
            Clicks = Convert.ToInt32(stats?.Clicks ?? 0),
            Ctr = stats?.Ctr ?? 0,
            TopKeywords = topKeywords.Select(k => new TopKeywordDto 
            { 
                Phrase = k.Phrase, 
                Spend = Convert.ToDouble(k.Spend ?? 0), 
                Revenue = Convert.ToDouble(k.Revenue ?? 0), 
                Drr = k.Drr ?? 0 
            }).ToList(),
            WorstKeywords = worstKeywords.Select(k => new TopKeywordDto 
            { 
                Phrase = k.Phrase, 
                Spend = Convert.ToDouble(k.Spend ?? 0), 
                Revenue = Convert.ToDouble(k.Revenue ?? 0), 
                Drr = k.Drr ?? 0 
            }).ToList()
        };

        // 2. Формирование промпта
        var prompt = $@"
Ты эксперт по рекламе на Wildberries с практическим опытом оптимизации рекламных кампаний.

Проанализируй рекламную кампанию и предложи ОДНУ самую важную рекомендацию для достижения цели: {goal}

Данные по кампании:
- Расход: {dto.Spend}
- Выручка: {dto.Revenue}
- ДРР: {dto.Drr}%
- Клики: {dto.Clicks}
- CTR: {dto.Ctr}%

Лучшие ключевые слова (по выручке):
{FormatKeywords(dto.TopKeywords)}

Худшие ключевые слова (высокий расход без заказов):
{FormatKeywords(dto.WorstKeywords)}

Контекст:
- DRR > 30% считается плохим
- Если много кликов и нет заказов — проблема в нерелевантных ключах
- Низкий CTR (< 2%) — проблема в карточке товара или креативах

Задача:
Определи ГЛАВНУЮ проблему кампании и предложи ОДНО конкретное действие, которое даст максимальный эффект.

Формат ответа:
1. Проблема: [описание]
2. Рекомендация: [описание]
3. Ожидаемый эффект: [описание]

Ограничения:
- Не давай общие советы
- Не перечисляй несколько действий
- Будь максимально конкретным
";

        // 3. Запрос к LLM - используем HttpClientFactory для создания клиента
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("OpenAIClient");
        
        // Устанавливаем заголовок авторизации
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = "Ты эксперт по рекламе Wildberries." },
                new { role = "user", content = prompt }
            },
            temperature = 0.7
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Отправка запроса к LLM для кампании {CampaignId} с целью {Goal}", campaignId, goal);
        var response = await httpClient.PostAsync(_baseUrl, content);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ошибка при запросе к LLM: {StatusCode} - {ReasonPhrase}", 
                (int)response.StatusCode, response.ReasonPhrase);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var llmResponse = JsonSerializer.Deserialize<LlmResponse>(responseJson);
        var resultText = llmResponse?.choices?.FirstOrDefault()?.message?.content;
        
        // Логирование ответа для отладки
        _logger.LogInformation("Получен ответ LLM длиной {Length} символов", resultText?.Length ?? 0);
        _logger.LogDebug("===== РЕКОМЕНДАЦИЯ =====\n{Result}", resultText);

        if (string.IsNullOrWhiteSpace(resultText)) return null;

        // Парсинг ответа (простая попытка разбить по строкам 1. 2. 3.)
        var lines = resultText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? problem = null, recText = null, effect = null;
        
        foreach (var line in lines)
        {
            if (line.StartsWith("1. Проблема:") || line.StartsWith("1. Проблема")) 
                problem = line.Substring(line.IndexOf(':') + 1).Trim();
            else if (line.StartsWith("2. Рекомендация:") || line.StartsWith("2. Рекомендация")) 
                recText = line.Substring(line.IndexOf(':') + 1).Trim();
            else if (line.StartsWith("3. Ожидаемый эффект:") || line.StartsWith("3. Ожидаемый эффект")) 
                effect = line.Substring(line.IndexOf(':') + 1).Trim();
        }

        // 4. Сохранение в БД
        var recommendation = new Recommendation
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            CreatedAt = DateTime.UtcNow,
            Goal = goal,
            Prompt = prompt,
            FullResponse = resultText ?? string.Empty,
            Problem = problem ?? string.Empty,
            RecommendationText = recText ?? string.Empty,
            ExpectedEffect = effect ?? string.Empty,
            Status = "новая",
            // Сохраняем метаданные запроса (модель, температура и т.д.)
            RequestMetadata = JsonSerializer.Serialize(new { 
                model = _model, 
                temperature = 0.7,
                timestamp = DateTime.UtcNow 
            }),
            // Добавляем пустой JSON объект для additional_data
            AdditionalData = JsonSerializer.Serialize(new {}),
            // Убедимся, что UserComment тоже не NULL
            UserComment = string.Empty
        };

        dbContext.Recommendations.Add(recommendation);
        await dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Рекомендация сохранена в БД: {RecommendationId} для кампании {CampaignId}", 
            recommendation.Id, campaignId);

        return recommendation;
    }

    private string FormatKeywords(IEnumerable<TopKeywordDto> keywords)
    {
        return string.Join("\n", keywords.Select(k =>
            $"- {k.Phrase}: расход={k.Spend}, выручка={k.Revenue}, ДРР={k.Drr}%"));
    }
}
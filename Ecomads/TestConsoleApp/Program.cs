using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;

// ================= CONFIG =================
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection");
var apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6IjNkY2U1MDczLTVhNDQtNGRlMS1iNTExLTI2MDM4MzVhMTYxMiIsImlzRGV2ZWxvcGVyIjp0cnVlLCJpYXQiOjE3NzM5MDE3MTksImV4cCI6MjA4OTQ3NzcxOSwianRpIjoiLUpuRmUxYk8xamxlRDBFMSJ9.PHJOQeLYoeuhn097QA-w0F1fsV5K4IBoSNT8TNQ2W30";
var baseUrl = "https://openai.bothub.chat/v1/chat/completions";
var model = "gpt-4o-mini";

// ================= DB SETUP =================
var services = new ServiceCollection();
services.AddDbContext<EcomadsDbContext>(options =>
    options.UseNpgsql(connectionString));

var serviceProvider = services.BuildServiceProvider();
var dbContext = serviceProvider.GetRequiredService<EcomadsDbContext>();

// Замени на нужный ID
var campaignId = Guid.Parse("d2a3565f-656a-4b18-959f-57181d43c7a5"); 

// ================= DATA FROM DB =================
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
    Name = (await dbContext.Compaigns.FindAsync(campaignId))?.Name ?? "Unknown",
    Spend = stats?.Spend ?? 0,
    Revenue = stats?.Revenue ?? 0,
    Drr = stats?.Drr ?? 0,
    Clicks = (int)(stats?.Clicks ?? 0),
    Ctr = stats?.Ctr ?? 0,

    TopKeywords = topKeywords.Select(k => new TopKeywordDto 
    { 
        Phrase = k.Phrase, 
        Spend = (double)(k.Spend ?? 0), 
        Revenue = (double)(k.Revenue ?? 0), 
        Drr = k.Drr ?? 0 
    }).ToList(),

    WorstKeywords = worstKeywords.Select(k => new TopKeywordDto 
    { 
        Phrase = k.Phrase, 
        Spend = (double)(k.Spend ?? 0), 
        Revenue = (double)(k.Revenue ?? 0), 
        Drr = k.Drr ?? 0 
    }).ToList()
};


// ================= BUILD PROMPT =================
var goal = "рост прибыли"; //увеличить заказы // рост прибыли

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
1. Проблема
2. Рекомендация
3. Ожидаемый эффект

Ограничения:
- Не давай общие советы
- Не перечисляй несколько действий
- Будь максимально конкретным
";
// ================= CALL LLM =================
using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

var requestBody = new
{
    model = model,
    messages = new[]
    {
        new { role = "system", content = "Ты эксперт по рекламе Wildberries." },
        new { role = "user", content = prompt }
    },
    temperature = 0.7
};

var json = JsonSerializer.Serialize(requestBody);
var content = new StringContent(json, Encoding.UTF8, "application/json");

var response = await http.PostAsync(baseUrl, content);

if (!response.IsSuccessStatusCode)
{
    Console.WriteLine($"Error: {response.StatusCode}");
    Console.WriteLine(await response.Content.ReadAsStringAsync());
    return;
}

var responseJson = await response.Content.ReadAsStringAsync();

var llmResponse = JsonSerializer.Deserialize<LlmResponse>(responseJson);

var result = llmResponse?.choices?[0]?.message?.content;

Console.WriteLine("===== РЕКОМЕНДАЦИЯ =====");
Console.WriteLine(result);

// ================= HELPERS =================
string FormatKeywords(IEnumerable<TopKeywordDto> keywords)
{
    return string.Join("\n", keywords.Select(k =>
        $"- {k.Phrase}: расход={k.Spend}, выручка={k.Revenue}, ДРР={k.Drr}%"));
}

// ================= MODELS =================
class CampaignAnalyticsDto
{
    public string Name { get; set; }
    public double Spend { get; set; }
    public double Revenue { get; set; }
    public double Drr { get; set; }
    public int Clicks { get; set; }
    public double Ctr { get; set; }
    public List<TopKeywordDto> TopKeywords { get; set; }
    public List<TopKeywordDto> WorstKeywords { get; set; }
}

class TopKeywordDto
{
    public string Phrase { get; set; }
    public double Spend { get; set; }
    public double Revenue { get; set; }
    public double Drr { get; set; }
}

class LlmResponse
{
    public List<Choice> choices { get; set; }

    public class Choice
    {
        public Message message { get; set; }
    }

    public class Message
    {
        public string content { get; set; }
    }
}
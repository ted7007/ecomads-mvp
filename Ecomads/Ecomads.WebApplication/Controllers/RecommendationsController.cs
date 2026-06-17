using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Models.Recommendations;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Analytics;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.Extensions.Logging;

namespace Ecomads.WebApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RecommendationsController : ControllerBase
    {
        private readonly EcomadsDbContext _context;
        private readonly IRecommendationService _recommendationService;
        private readonly IKeywordRecommendationOverlayService _keywordRecommendationOverlayService;
        private readonly IInsightDecisionService _insightDecisionService;
        private readonly IProductAnalyticsService _analyticsService;
        private readonly ILogger<RecommendationsController> _logger;

        public RecommendationsController(
            EcomadsDbContext context, 
            IRecommendationService recommendationService,
            IKeywordRecommendationOverlayService keywordRecommendationOverlayService,
            IInsightDecisionService insightDecisionService,
            IProductAnalyticsService analyticsService,
            ILogger<RecommendationsController> logger)
        {
            _context = context;
            _recommendationService = recommendationService;
            _keywordRecommendationOverlayService = keywordRecommendationOverlayService;
            _insightDecisionService = insightDecisionService;
            _analyticsService = analyticsService;
            _logger = logger;
        }

        /// <summary>
        /// Получить таблицу ключевых слов с рекомендациями и деталями инсайтов.
        /// </summary>
        [HttpGet("campaign/{campaignId}/keyword-overlay")]
        public async Task<IActionResult> GetKeywordRecommendationOverlay(
            Guid campaignId,
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!TryGetCurrentSellerId(out var sellerId))
                {
                    return Unauthorized(new { message = "Недействительный токен" });
                }

                if (!await SellerOwnsCampaignAsync(sellerId, campaignId, cancellationToken))
                {
                    return NotFound($"Кампания с ID {campaignId} не найдена");
                }

                var overlay = await _keywordRecommendationOverlayService.GetOverlayAsync(
                    campaignId,
                    startDate,
                    endDate,
                    cancellationToken);

                if (overlay == null)
                {
                    return NotFound($"Кампания с ID {campaignId} не найдена");
                }

                return Ok(overlay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении recommendation overlay для кампании {CampaignId}", campaignId);
                return StatusCode(500, $"Ошибка при получении recommendation overlay: {ex.Message}");
            }
        }

        /// <summary>
        /// Принять insight рекомендации.
        /// </summary>
        [HttpPost("insights/{insightId}/accept")]
        public Task<IActionResult> AcceptInsight(string insightId, CancellationToken cancellationToken)
        {
            return UpdateInsightDecision(insightId, InsightDecisionStatus.Accepted, cancellationToken);
        }

        /// <summary>
        /// Отложить insight рекомендации.
        /// </summary>
        [HttpPost("insights/{insightId}/postpone")]
        public Task<IActionResult> PostponeInsight(string insightId, CancellationToken cancellationToken)
        {
            return UpdateInsightDecision(insightId, InsightDecisionStatus.Postponed, cancellationToken);
        }

        /// <summary>
        /// Отклонить insight рекомендации.
        /// </summary>
        [HttpPost("insights/{insightId}/reject")]
        public Task<IActionResult> RejectInsight(string insightId, CancellationToken cancellationToken)
        {
            return UpdateInsightDecision(insightId, InsightDecisionStatus.Rejected, cancellationToken);
        }

        /// <summary>
        /// Отметить insight рекомендации как примененный.
        /// </summary>
        [HttpPost("insights/{insightId}/apply")]
        public Task<IActionResult> ApplyInsight(string insightId, CancellationToken cancellationToken)
        {
            return UpdateInsightDecision(insightId, InsightDecisionStatus.Applied, cancellationToken);
        }

        /// <summary>
        /// Обновить комментарий пользователя по insight.
        /// </summary>
        [HttpPut("insights/{insightId}/comment")]
        public async Task<IActionResult> UpdateInsightComment(
            string insightId,
            [FromBody] UpdateInsightCommentRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(insightId))
            {
                return BadRequest("Необходимо указать insightId");
            }

            try
            {
                if (!TryGetCurrentSellerId(out var sellerId))
                {
                    return Unauthorized(new { message = "Недействительный токен" });
                }

                if (!await SellerOwnsInsightAsync(sellerId, insightId, cancellationToken))
                {
                    return NotFound($"Insight с ID {insightId} не найден");
                }

                var result = await _insightDecisionService.UpdateCommentAsync(
                    insightId,
                    request?.UserComment,
                    cancellationToken);

                if (result == null)
                {
                    return NotFound($"Insight с ID {insightId} не найден");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении комментария insight {InsightId}", insightId);
                return StatusCode(500, $"Ошибка при обновлении комментария insight: {ex.Message}");
            }
        }

        private async Task<IActionResult> UpdateInsightDecision(
            string insightId,
            InsightDecisionStatus decisionStatus,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(insightId))
            {
                return BadRequest("Необходимо указать insightId");
            }

            try
            {
                if (!TryGetCurrentSellerId(out var sellerId))
                {
                    return Unauthorized(new { message = "Недействительный токен" });
                }

                if (!await SellerOwnsInsightAsync(sellerId, insightId, cancellationToken))
                {
                    return NotFound($"Insight с ID {insightId} не найден");
                }

                var result = await _insightDecisionService.UpdateDecisionAsync(
                    insightId,
                    decisionStatus,
                    cancellationToken);

                if (result == null)
                {
                    return NotFound($"Insight с ID {insightId} не найден");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении решения по insight {InsightId}", insightId);
                return StatusCode(500, $"Ошибка при обновлении решения по insight: {ex.Message}");
            }
        }

        /// <summary>
        /// Получить все рекомендации для указанной кампании, отсортированные по дате создания (от новых к старым)
        /// </summary>
        [HttpGet("campaign/{campaignId}")]
        public async Task<ActionResult<IEnumerable<Recommendation>>> GetCampaignRecommendations(Guid campaignId)
        {
            if (!TryGetCurrentSellerId(out var sellerId))
            {
                return Unauthorized(new { message = "Недействительный токен" });
            }

            if (!await SellerOwnsCampaignAsync(sellerId, campaignId))
            {
                return NotFound($"Кампания с ID {campaignId} не найдена");
            }

            var recommendations = await _context.Recommendations
                .Where(r => r.CampaignId == campaignId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Ok(recommendations);
        }

        /// <summary>
        /// Получить конкретную рекомендацию по ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<Recommendation>> GetRecommendation(Guid id)
        {
            if (!TryGetCurrentSellerId(out var sellerId))
            {
                return Unauthorized(new { message = "Недействительный токен" });
            }

            var recommendation = await _context.Recommendations
                .Include(item => item.Campaign)
                    .ThenInclude(campaign => campaign.Store)
                .FirstOrDefaultAsync(item =>
                    item.Id == id
                    && item.Campaign.Store.SellerId == sellerId);

            if (recommendation == null)
            {
                return NotFound($"Рекомендация с ID {id} не найдена");
            }

            await _analyticsService.TrackAsync(new ProductUsageEventCreateDto
            {
                UserId = sellerId,
                EventName = ProductEvents.KeywordRecommendationOpened,
                FeatureName = ProductFeatures.KeywordRecommendations,
                CampaignId = recommendation.CampaignId,
                Metadata = new
                {
                    recommendationId = recommendation.Id,
                    recommendationStatus = recommendation.Status,
                    source = GetRecommendationSource(recommendation)
                }
            }.WithRequestContext(HttpContext));

            _logger.LogInformation(
                "Recommendation opened by user {UserId}. RecommendationId: {RecommendationId}, CampaignId: {CampaignId}, Status: {Status}",
                sellerId,
                recommendation.Id,
                recommendation.CampaignId,
                recommendation.Status);

            return Ok(recommendation);
        }

        /// <summary>
        /// Сгенерировать новую рекомендацию для кампании с указанной целью
        /// </summary>
        [HttpPost("generate")]
        public async Task<ActionResult<Recommendation>> GenerateRecommendation([FromBody] GenerateRecommendationRequest request)
        {
            if (request == null || request.CampaignId == Guid.Empty)
            {
                return BadRequest("Необходимо указать CampaignId");
            }

            if (!TryGetCurrentSellerId(out var sellerId))
            {
                return Unauthorized(new { message = "Недействительный токен" });
            }

            if (!await SellerOwnsCampaignAsync(sellerId, request.CampaignId))
            {
                return NotFound($"Кампания с ID {request.CampaignId} не найдена");
            }

            try
            {
                var goal = !string.IsNullOrWhiteSpace(request.Goal) ? request.Goal : "рост прибыли";
                var recommendation = await _recommendationService.GenerateRecommendationAsync(request.CampaignId, goal);

                if (recommendation == null)
                {
                    return StatusCode(500, "Не удалось сгенерировать рекомендацию");
                }

                return Ok(recommendation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при генерации рекомендации для кампании {CampaignId}", request.CampaignId);
                return StatusCode(500, $"Ошибка при генерации рекомендации: {ex.Message}");
            }
        }

        /// <summary>
        /// Обновить статус рекомендации
        /// </summary>
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateRecommendationStatus(Guid id, [FromBody] UpdateRecommendationStatusRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
            {
                return BadRequest("Необходимо указать статус");
            }

            if (!TryGetCurrentSellerId(out var sellerId))
            {
                return Unauthorized(new { message = "Недействительный токен" });
            }

            var recommendation = await _context.Recommendations
                .Include(item => item.Campaign)
                    .ThenInclude(campaign => campaign.Store)
                .FirstOrDefaultAsync(item =>
                    item.Id == id
                    && item.Campaign.Store.SellerId == sellerId);

            if (recommendation == null)
            {
                return NotFound($"Рекомендация с ID {id} не найдена");
            }

            recommendation.Status = request.Status;
            recommendation.StatusUpdatedAt = DateTime.UtcNow;
            
            if (!string.IsNullOrWhiteSpace(request.UserComment))
            {
                recommendation.UserComment = request.UserComment;
            }

            try
            {
                await _context.SaveChangesAsync();
                return Ok(recommendation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении статуса рекомендации {RecommendationId}", id);
                return StatusCode(500, $"Ошибка при обновлении статуса рекомендации: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Получить статистику по рекомендациям за указанный период
        /// </summary>
        /// <param name="period">Период для статистики: week, month, quarter, year</param>
        /// <returns>Статистика по рекомендациям</returns>
        [HttpGet("stats")]
        public async Task<ActionResult<RecommendationStatsResponse>> GetRecommendationsStats([FromQuery] string period = "month")
        {
            try
            {
                if (!TryGetCurrentSellerId(out var sellerId))
                {
                    return Unauthorized(new { message = "Недействительный токен" });
                }

                var sellerCampaignIds = await GetSellerCampaignIdsAsync(sellerId);

                // Определяем начальную дату в зависимости от периода
                DateTime startDate = period.ToLower() switch
                {
                    "week" => DateTime.UtcNow.AddDays(-7),
                    "month" => DateTime.UtcNow.AddDays(-30),
                    "quarter" => DateTime.UtcNow.AddDays(-90),
                    "year" => DateTime.UtcNow.AddDays(-365),
                    _ => DateTime.UtcNow.AddDays(-30) // По умолчанию - месяц
                };

                var insights = await _context.RecommendationInsights
                    .Where(insight =>
                        insight.CreatedAt >= startDate
                        && sellerCampaignIds.Contains(insight.CampaignId))
                    .Include(insight => insight.RecommendationRun)
                        .ThenInclude(run => run.Campaign)
                    .OrderByDescending(insight => insight.UpdatedAt)
                    .ToListAsync();

                var response = new RecommendationStatsResponse();

                response.Counts = new RecommendationCounts
                {
                    Accepted = insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Accepted),
                    Pending = insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Postponed),
                    Rejected = insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Rejected),
                    Applied = insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Applied)
                };

                response.ExpectedSaving = insights
                    .Where(insight => insight.ExpectedEffectType == ExpectedEffectType.Saving)
                    .Sum(insight => insight.ExpectedEffectMoney ?? 0m);
                response.ExpectedAdditionalRevenue = insights
                    .Where(insight => insight.ExpectedEffectType == ExpectedEffectType.AdditionalRevenue)
                    .Sum(insight => insight.ExpectedEffectMoney ?? 0m);
                response.NotCalculatedCount = insights.Count(insight =>
                    insight.ExpectedEffectType == ExpectedEffectType.NotCalculated);

                var monthlyData = insights
                    .GroupBy(insight => new { Year = insight.UpdatedAt.Year, Month = insight.UpdatedAt.Month })
                    .Select(g => new
                    {
                        YearMonth = g.Key,
                        Insights = g.ToList()
                    })
                    .OrderBy(g => g.YearMonth.Year)
                    .ThenBy(g => g.YearMonth.Month)
                    .ToList();

                foreach (var monthGroup in monthlyData)
                {
                    var accepted = monthGroup.Insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Accepted);
                    var pending = monthGroup.Insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Postponed);
                    var rejected = monthGroup.Insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Rejected);
                    var applied = monthGroup.Insights.Count(insight => insight.DecisionStatus == InsightDecisionStatus.Applied);

                    response.Monthly.Add(new MonthlyStats
                    {
                        Month = new DateTime(monthGroup.YearMonth.Year, monthGroup.YearMonth.Month, 1)
                            .ToString("MMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),
                        Accepted = accepted,
                        Pending = pending,
                        Rejected = rejected,
                        Applied = applied,
                        Total = accepted + pending + rejected + applied
                    });
                }

                var visibleInsights = insights
                    .Where(insight => insight.DecisionStatus is
                        InsightDecisionStatus.Accepted or
                        InsightDecisionStatus.Postponed or
                        InsightDecisionStatus.Applied)
                    .Take(20)
                    .ToList();

                foreach (var insight in visibleInsights)
                {
                    var actualEffect = await CalculateActualEffectAsync(insight);
                    response.Recommendations.Add(new RecommendationDetail
                    {
                        Id = insight.Id,
                        Text = insight.ExpectedEffectText,
                        EntityName = insight.EntityName,
                        Action = FormatAction(insight.RecommendedAction),
                        Status = insight.DecisionStatus.ToString(),
                        Date = insight.UpdatedAt,
                        Campaign = insight.RecommendationRun.Campaign?.Name ?? "Неизвестная кампания",
                        Comment = insight.UserComment ?? string.Empty,
                        ExpectedEffectType = insight.ExpectedEffectType.ToString(),
                        ExpectedEffectMoney = insight.ExpectedEffectMoney,
                        ExpectedEffectText = insight.ExpectedEffectText,
                        ActualEffectMoney = actualEffect.Money,
                        ActualEffectStatus = actualEffect.Status,
                        ActualEffectText = actualEffect.Text,
                        Period = $"{insight.PeriodFrom:dd.MM.yyyy} - {insight.PeriodTo:dd.MM.yyyy}"
                    });
                }

                await _analyticsService.TrackAsync(new ProductUsageEventCreateDto
                {
                    UserId = sellerId,
                    EventName = ProductEvents.ExpectedEffectPageViewed,
                    FeatureName = ProductFeatures.ExpectedEffectPage,
                    Metadata = new
                    {
                        period,
                        insightsCount = insights.Count,
                        visibleRecommendationsCount = response.Recommendations.Count,
                        expectedSaving = response.ExpectedSaving,
                        expectedAdditionalRevenue = response.ExpectedAdditionalRevenue,
                        notCalculatedCount = response.NotCalculatedCount
                    }
                }.WithRequestContext(HttpContext));

                _logger.LogInformation(
                    "Expected effect page opened by user {UserId}. Period: {Period}, InsightsCount: {InsightsCount}, VisibleRecommendationsCount: {VisibleRecommendationsCount}",
                    sellerId,
                    period,
                    insights.Count,
                    response.Recommendations.Count);

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики по рекомендациям");
                return StatusCode(500, $"Ошибка при получении статистики: {ex.Message}");
            }
        }

        private async Task<ActualEffectResult> CalculateActualEffectAsync(RecommendationInsightEntity insight)
        {
            if (insight.ExpectedEffectType != ExpectedEffectType.Saving
                || insight.EntityType != InsightEntityType.Keyword)
            {
                return new ActualEffectResult(null, "NotApplicable", "Фактический эффект не рассчитывается для этого типа insight.");
            }

            var previousSpend = GetMetric(insight.MetricsJson, "spend");
            if (!previousSpend.HasValue)
            {
                return new ActualEffectResult(null, "NotCalculated", "Нет исходного расхода для сравнения.");
            }

            var nextStats = await _context.KeywordStatistics
                .Where(keyword =>
                    keyword.CompaignId == insight.CampaignId
                    && keyword.Phrase == insight.EntityName
                    && keyword.StartDate > insight.PeriodTo)
                .OrderBy(keyword => keyword.StartDate)
                .FirstOrDefaultAsync();

            if (nextStats == null)
            {
                return new ActualEffectResult(null, "WaitingForNextStats", "Ожидаем статистику следующего периода.");
            }

            var actualSaving = previousSpend.Value - (nextStats.Spend ?? 0m);
            return new ActualEffectResult(
                actualSaving,
                "Calculated",
                "Оценочная экономия по изменению расхода");
        }

        private static decimal? GetMetric(string metricsJson, string key)
        {
            try
            {
                var metrics = JsonSerializer.Deserialize<Dictionary<string, decimal?>>(metricsJson);
                return metrics != null && metrics.TryGetValue(key, out var value)
                    ? value
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string FormatAction(RecommendationAction? action)
        {
            return action switch
            {
                RecommendationAction.ConsiderMinusKeyword => "Исключить",
                RecommendationAction.MinusKeyword => "Минус-слово",
                RecommendationAction.DecreaseBid => "Снизить ставку",
                RecommendationAction.DecreaseBidCarefully => "Снизить осторожно",
                RecommendationAction.IncreaseBidGradually => "Повысить ставку",
                RecommendationAction.Scale => "Масштабировать",
                RecommendationAction.CollectMoreData => "Собрать данные",
                RecommendationAction.Watch => "Наблюдать",
                RecommendationAction.Optimize => "Оптимизировать",
                RecommendationAction.Maintain => "Сохранить",
                RecommendationAction.FindSimilarKeywords => "Найти похожие",
                null => "-",
                _ => action.ToString() ?? "-"
            };
        }

        private static string GetRecommendationSource(Recommendation recommendation)
        {
            try
            {
                using var document = JsonDocument.Parse(recommendation.AdditionalData);
                return document.RootElement.TryGetProperty("generatedWithoutLlm", out var generatedWithoutLlm) &&
                    generatedWithoutLlm.ValueKind == JsonValueKind.True
                    ? "deterministic"
                    : "llm";
            }
            catch (JsonException)
            {
                return "unknown";
            }
        }

        private bool TryGetCurrentSellerId(out Guid sellerId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userId, out sellerId);
        }

        private async Task<List<Guid>> GetSellerCampaignIdsAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Compaigns
                .Where(campaign => campaign.Store.SellerId == sellerId)
                .Select(campaign => campaign.Id)
                .ToListAsync(cancellationToken);
        }

        private async Task<bool> SellerOwnsCampaignAsync(
            Guid sellerId,
            Guid campaignId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Compaigns
                .AnyAsync(
                    campaign => campaign.Id == campaignId
                        && campaign.Store.SellerId == sellerId,
                    cancellationToken);
        }

        private async Task<bool> SellerOwnsInsightAsync(
            Guid sellerId,
            string insightId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecommendationInsights
                .AnyAsync(
                    insight => insight.Id == insightId
                        && insight.RecommendationRun.Campaign.Store.SellerId == sellerId,
                    cancellationToken);
        }
    }

    public class GenerateRecommendationRequest
    {
        public Guid CampaignId { get; set; }
        public string Goal { get; set; }
    }

    public class UpdateRecommendationStatusRequest
    {
        public string Status { get; set; }
        public string UserComment { get; set; }
    }
    
    public class RecommendationStatsResponse
    {
        public RecommendationCounts Counts { get; set; } = new RecommendationCounts();
        public List<MonthlyStats> Monthly { get; set; } = new List<MonthlyStats>();
        public List<RecommendationDetail> Recommendations { get; set; } = new List<RecommendationDetail>();
        public decimal ExpectedSaving { get; set; }
        public decimal ExpectedAdditionalRevenue { get; set; }
        public int NotCalculatedCount { get; set; }
    }
    
    public class RecommendationCounts
    {
        public int Accepted { get; set; }
        public int Pending { get; set; }
        public int Rejected { get; set; }
        public int Applied { get; set; }
    }
    
    public class MonthlyStats
    {
        public string Month { get; set; }
        public int Accepted { get; set; }
        public int Pending { get; set; }
        public int Rejected { get; set; }
        public int Applied { get; set; }
        public int Total { get; set; }
    }
    
    public class RecommendationDetail
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string EntityName { get; set; }
        public string Action { get; set; }
        public string Status { get; set; }
        public DateTime Date { get; set; }
        public string Campaign { get; set; }
        public string Comment { get; set; }
        public string ExpectedEffectType { get; set; }
        public decimal? ExpectedEffectMoney { get; set; }
        public string ExpectedEffectText { get; set; }
        public decimal? ActualEffectMoney { get; set; }
        public string ActualEffectStatus { get; set; }
        public string ActualEffectText { get; set; }
        public string Period { get; set; }
    }

    public record ActualEffectResult(decimal? Money, string Status, string Text);
}

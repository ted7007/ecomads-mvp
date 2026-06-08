using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models.Recommendations;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.Extensions.Logging;

namespace Ecomads.WebApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecommendationsController : ControllerBase
    {
        private readonly EcomadsDbContext _context;
        private readonly IRecommendationService _recommendationService;
        private readonly IKeywordRecommendationOverlayService _keywordRecommendationOverlayService;
        private readonly IInsightDecisionService _insightDecisionService;
        private readonly ILogger<RecommendationsController> _logger;

        public RecommendationsController(
            EcomadsDbContext context, 
            IRecommendationService recommendationService,
            IKeywordRecommendationOverlayService keywordRecommendationOverlayService,
            IInsightDecisionService insightDecisionService,
            ILogger<RecommendationsController> logger)
        {
            _context = context;
            _recommendationService = recommendationService;
            _keywordRecommendationOverlayService = keywordRecommendationOverlayService;
            _insightDecisionService = insightDecisionService;
            _logger = logger;
        }

        /// <summary>
        /// Получить таблицу ключевых слов с рекомендациями и деталями инсайтов.
        /// </summary>
        [HttpGet("campaign/{campaignId}/keyword-overlay")]
        public async Task<IActionResult> GetKeywordRecommendationOverlay(
            Guid campaignId,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            CancellationToken cancellationToken)
        {
            try
            {
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
            // Проверяем существование кампании
            var campaignExists = await _context.Compaigns.AnyAsync(c => c.Id == campaignId);
            if (!campaignExists)
            {
                return NotFound($"Кампания с ID {campaignId} не найдена");
            }

            // Получаем рекомендации, сортируем по дате создания (сначала новые)
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
            var recommendation = await _context.Recommendations.FindAsync(id);
            if (recommendation == null)
            {
                return NotFound($"Рекомендация с ID {id} не найдена");
            }

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

            // Проверяем существование кампании
            var campaignExists = await _context.Compaigns.AnyAsync(c => c.Id == request.CampaignId);
            if (!campaignExists)
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

            var recommendation = await _context.Recommendations.FindAsync(id);
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
                // Определяем начальную дату в зависимости от периода
                DateTime startDate = period.ToLower() switch
                {
                    "week" => DateTime.UtcNow.AddDays(-7),
                    "month" => DateTime.UtcNow.AddDays(-30),
                    "quarter" => DateTime.UtcNow.AddDays(-90),
                    "year" => DateTime.UtcNow.AddDays(-365),
                    _ => DateTime.UtcNow.AddDays(-30) // По умолчанию - месяц
                };

                // Получаем рекомендации за выбранный период
                var recommendations = await _context.Recommendations
                    .Where(r => r.CreatedAt >= startDate)
                    .Include(r => r.Campaign)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync();

                // Подсчитываем статистику
                var response = new RecommendationStatsResponse();

                // Общие счетчики по статусам
                response.Counts = new RecommendationCounts
                {
                    Accepted = recommendations.Count(r => r.Status == "принята"),
                    Pending = recommendations.Count(r => r.Status == "новая"),
                    Rejected = recommendations.Count(r => r.Status == "отклонена")
                };

                // Статистика по месяцам для графика
                // Группируем данные по году и месяцу
                var monthlyData = recommendations
                    .GroupBy(r => new { Year = r.CreatedAt.Year, Month = r.CreatedAt.Month })
                    .Select(g => new
                    {
                        YearMonth = g.Key,
                        Recommendations = g.ToList()
                    })
                    .OrderBy(g => g.YearMonth.Year)
                    .ThenBy(g => g.YearMonth.Month)
                    .ToList();

                // Конвертируем в модель для фронтенда
                foreach (var monthGroup in monthlyData)
                {
                    var accepted = monthGroup.Recommendations.Count(r => r.Status == "принята");
                    var pending = monthGroup.Recommendations.Count(r => r.Status == "новая");
                    var rejected = monthGroup.Recommendations.Count(r => r.Status == "отклонена");

                    response.Monthly.Add(new MonthlyStats
                    {
                        Month = new DateTime(monthGroup.YearMonth.Year, monthGroup.YearMonth.Month, 1)
                            .ToString("MMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),
                        Accepted = accepted,
                        Pending = pending,
                        Rejected = rejected,
                        Total = accepted + pending + rejected
                    });
                }

                // Список последних рекомендаций (ограничиваем 20)
                response.Recommendations = recommendations
                    .Take(20)
                    .Select(r => new RecommendationDetail
                    {
                        Id = r.Id,
                        Text = r.RecommendationText,
                        Status = r.Status,
                        Date = r.CreatedAt,
                        Campaign = r.Campaign?.Name ?? "Неизвестная кампания",
                        Comment = r.UserComment
                    })
                    .ToList();

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики по рекомендациям");
                return StatusCode(500, $"Ошибка при получении статистики: {ex.Message}");
            }
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
    }
    
    public class RecommendationCounts
    {
        public int Accepted { get; set; }
        public int Pending { get; set; }
        public int Rejected { get; set; }
    }
    
    public class MonthlyStats
    {
        public string Month { get; set; }
        public int Accepted { get; set; }
        public int Pending { get; set; }
        public int Rejected { get; set; }
        public int Total { get; set; }
    }
    
    public class RecommendationDetail
    {
        public Guid Id { get; set; }
        public string Text { get; set; }
        public string Status { get; set; }
        public DateTime Date { get; set; }
        public string Campaign { get; set; }
        public string Comment { get; set; }
    }
}

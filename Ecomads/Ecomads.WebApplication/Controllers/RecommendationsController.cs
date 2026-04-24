using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services;
using Microsoft.Extensions.Logging;

namespace Ecomads.WebApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecommendationsController : ControllerBase
    {
        private readonly EcomadsDbContext _context;
        private readonly IRecommendationService _recommendationService;
        private readonly ILogger<RecommendationsController> _logger;

        public RecommendationsController(
            EcomadsDbContext context, 
            IRecommendationService recommendationService,
            ILogger<RecommendationsController> logger)
        {
            _context = context;
            _recommendationService = recommendationService;
            _logger = logger;
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
}
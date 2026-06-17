using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/demo-feedback")]
[Authorize]
public class DemoFeedbackController : ControllerBase
{
    private const string DashboardRedirectPath = "/dashboard";

    private static readonly HashSet<string> PrimaryTaskOptions = new(StringComparer.Ordinal)
    {
        "reduce_drr",
        "find_ineffective_keywords",
        "find_scale_queries",
        "estimate_expected_effect",
        "understand_campaign_stats",
        "other"
    };

    private static readonly HashSet<string> FeatureOptions = new(StringComparer.Ordinal)
    {
        "statistics_upload",
        "campaign_summary",
        "keyword_recommendations",
        "expected_effect",
        "keyword_details"
    };

    private static readonly HashSet<string> MostUsefulFeatureOptions = new(StringComparer.Ordinal)
    {
        "statistics_upload",
        "campaign_summary",
        "keyword_recommendations",
        "expected_effect",
        "keyword_details",
        "nothing_useful"
    };

    private static readonly HashSet<string> MissingForDecisionOptions = new(StringComparer.Ordinal)
    {
        "more_recommendation_explanations",
        "more_keyword_data",
        "money_effect_forecast",
        "before_after_comparison",
        "wb_action_instruction",
        "easier_report_upload",
        "nothing_missing",
        "other"
    };

    private static readonly HashSet<string> ContinueUsingOptions = new(StringComparer.Ordinal)
    {
        "yes",
        "maybe_after_improvements",
        "no"
    };

    private readonly EcomadsDbContext _dbContext;
    private readonly IUserAccessService _userAccessService;
    private readonly IProductAnalyticsService _analyticsService;
    private readonly ILogger<DemoFeedbackController> _logger;

    public DemoFeedbackController(
        EcomadsDbContext dbContext,
        IUserAccessService userAccessService,
        IProductAnalyticsService analyticsService,
        ILogger<DemoFeedbackController> logger)
    {
        _dbContext = dbContext;
        _userAccessService = userAccessService;
        _analyticsService = analyticsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrentFeedbackState()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }

        var seller = await _dbContext.Sellers.FirstOrDefaultAsync(x => x.Id == userId.Value);
        if (seller == null)
        {
            return NotFound(new { message = "Пользователь не найден" });
        }

        var accessState = await _userAccessService.GetAccessStateAsync(userId.Value);

        var feedback = await _dbContext.DemoFeedbacks
            .Where(x => x.UserId == userId.Value)
            .Select(x => new
            {
                x.Id,
                x.CreatedAtUtc
            })
            .FirstOrDefaultAsync();

        _logger.LogInformation(
            "Demo feedback page opened by user {UserId}. HasSubmitted: {HasSubmitted}",
            userId.Value,
            feedback != null);

        await _analyticsService.TrackAsync(new ProductUsageEventCreateDto
        {
            UserId = userId.Value,
            EventName = ProductEvents.DemoFeedbackViewed,
            FeatureName = ProductFeatures.DemoFeedback,
            Metadata = new
            {
                hasSubmitted = feedback != null,
                canSubmit = seller.IsDemoUser && accessState.ShouldRequireDemoFeedback && feedback == null
            }
        }.WithRequestContext(HttpContext));

        return Ok(new
        {
            userId = seller.Id,
            isDemoUser = seller.IsDemoUser,
            accessType = seller.AccessType,
            demoStatus = seller.DemoStatus,
            hasSubmitted = feedback != null || seller.DemoStatus == DemoAccessStatus.FeedbackSubmitted,
            feedbackId = feedback?.Id,
            feedbackSubmittedAtUtc = feedback?.CreatedAtUtc ?? seller.DemoFeedbackSubmittedAtUtc,
            canSubmit = seller.IsDemoUser && accessState.ShouldRequireDemoFeedback && feedback == null
        });
    }

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] DemoFeedbackSubmitRequest request)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }

        ValidateRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var seller = await _dbContext.Sellers.FirstOrDefaultAsync(x => x.Id == userId.Value);
        if (seller == null)
        {
            return NotFound(new { message = "Пользователь не найден" });
        }

        if (!seller.IsDemoUser)
        {
            return Forbid();
        }

        if (seller.DemoStatus == DemoAccessStatus.FeedbackSubmitted ||
            await _dbContext.DemoFeedbacks.AnyAsync(x => x.UserId == userId.Value))
        {
            return Conflict(new { message = "Обратная связь уже отправлена" });
        }

        if (!await _userAccessService.ShouldRequireDemoFeedbackAsync(userId.Value))
        {
            return BadRequest(new { message = "Обратную связь нужно отправить после окончания demo-доступа." });
        }

        var feedback = new DemoFeedback
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            GeneralComment = request.GeneralComment!.Trim(),
            DashboardClarityScore = request.RecommendationsClarityScore,
            RecommendationsUsefulnessScore = request.RecommendationsClarityScore,
            WrongOrQuestionableRecommendations = SerializeFeedbackPart(new
            {
                usedSections = request.UsedSections,
                missingForDecision = request.MissingForDecision
            }),
            MostUsefulFeature = request.MostUsefulFeature!.Trim(),
            MissingForRegularUsage = SerializeFeedbackPart(new
            {
                primaryTask = request.PrimaryTask,
                improvementPriority = NormalizeOptionalText(request.ImprovementPriority)
            }),
            ContinueTestingAnswer = request.ContinueUsingAnswer!.Trim(),
            WillingToPayAnswer = "not_asked",
            CreatedAtUtc = DateTime.UtcNow
        };

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            _dbContext.DemoFeedbacks.Add(feedback);
            await _userAccessService.GrantMvpAccessAfterFeedbackAsync(userId.Value);

            await transaction.CommitAsync();
        });

        _logger.LogInformation(
            "Demo user {UserId} submitted feedback and received MVP access",
            userId.Value);

        await _analyticsService.TrackAsync(new ProductUsageEventCreateDto
        {
            UserId = userId.Value,
            EventName = ProductEvents.DemoFeedbackSubmitted,
            FeatureName = ProductFeatures.DemoFeedback,
            Metadata = new
            {
                feedbackId = feedback.Id,
                accessGranted = true,
                primaryTask = request.PrimaryTask,
                mostUsefulFeature = request.MostUsefulFeature,
                continueUsingAnswer = request.ContinueUsingAnswer
            }
        }.WithRequestContext(HttpContext));

        return Ok(new
        {
            message = "Спасибо за обратную связь. Доступ к MVP-версии открыт.",
            redirectTo = DashboardRedirectPath
        });
    }

    private void ValidateRequest(DemoFeedbackSubmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GeneralComment) || request.GeneralComment.Trim().Length < 50)
        {
            ModelState.AddModelError(nameof(request.GeneralComment), "Общий комментарий должен быть не короче 50 символов.");
        }

        if (string.IsNullOrWhiteSpace(request.PrimaryTask) ||
            !PrimaryTaskOptions.Contains(request.PrimaryTask.Trim()))
        {
            ModelState.AddModelError(nameof(request.PrimaryTask), "Выберите задачу, которую вы пытались решить.");
        }

        if (request.UsedSections == null || request.UsedSections.Count == 0 ||
            request.UsedSections.Any(option => !FeatureOptions.Contains(option)))
        {
            ModelState.AddModelError(nameof(request.UsedSections), "Выберите хотя бы один раздел, который вы успели использовать.");
        }

        if (request.RecommendationsClarityScore is < 1 or > 5)
        {
            ModelState.AddModelError(nameof(request.RecommendationsClarityScore), "Оцените понятность рекомендаций от 1 до 5.");
        }

        if (string.IsNullOrWhiteSpace(request.MostUsefulFeature) ||
            !MostUsefulFeatureOptions.Contains(request.MostUsefulFeature.Trim()))
        {
            ModelState.AddModelError(nameof(request.MostUsefulFeature), "Выберите самую полезную функцию.");
        }

        if (request.MissingForDecision == null || request.MissingForDecision.Count == 0 ||
            request.MissingForDecision.Any(option => !MissingForDecisionOptions.Contains(option)))
        {
            ModelState.AddModelError(nameof(request.MissingForDecision), "Выберите, чего не хватило для решения по рекламе.");
        }

        if (string.IsNullOrWhiteSpace(request.ContinueUsingAnswer) ||
            !ContinueUsingOptions.Contains(request.ContinueUsingAnswer.Trim()))
        {
            ModelState.AddModelError(nameof(request.ContinueUsingAnswer), "Выберите, хотите ли продолжить пользоваться EcomAds.");
        }

        if (request.ContinueUsingAnswer is "maybe_after_improvements" or "no" &&
            request.ImprovementPriority?.Length > 1000)
        {
            ModelState.AddModelError(nameof(request.ImprovementPriority), "Опишите доработки короче 1000 символов.");
        }
    }

    private Guid? GetCurrentUserId()
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string SerializeFeedbackPart(object value)
    {
        return JsonSerializer.Serialize(value);
    }
}

public class DemoFeedbackSubmitRequest
{
    [Required]
    public string? PrimaryTask { get; set; }

    [Required]
    public List<string> UsedSections { get; set; } = new();

    [Required]
    public string? MostUsefulFeature { get; set; }

    [Range(1, 5)]
    public int RecommendationsClarityScore { get; set; }

    [Required]
    public List<string> MissingForDecision { get; set; } = new();

    [Required]
    public string? GeneralComment { get; set; }

    [Required]
    public string? ContinueUsingAnswer { get; set; }

    public string? ImprovementPriority { get; set; }
}

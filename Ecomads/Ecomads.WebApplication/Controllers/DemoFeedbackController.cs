using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services;
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
        "find_waste",
        "configure_norms",
        "understand_campaign_stats",
        "other"
    };

    private static readonly HashSet<string> FeatureOptions = new(StringComparer.Ordinal)
    {
        "dashboard",
        "campaign_summary",
        "clusters",
        "norms",
        "telegram"
    };

    private static readonly HashSet<string> MostUsefulFeatureOptions = new(StringComparer.Ordinal)
    {
        "dashboard",
        "campaign_summary",
        "clusters",
        "norms",
        "telegram",
        "nothing_useful"
    };

    private static readonly HashSet<string> MissingForDecisionOptions = new(StringComparer.Ordinal)
    {
        "clearer_metrics",
        "longer_history",
        "recommendations",
        "telegram_updates",
        "wb_action_instruction",
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
    private readonly ILogger<DemoFeedbackController> _logger;

    public DemoFeedbackController(
        EcomadsDbContext dbContext,
        IUserAccessService userAccessService,
        ILogger<DemoFeedbackController> logger)
    {
        _dbContext = dbContext;
        _userAccessService = userAccessService;
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
            AnswersJson = SerializeFeedbackPart(new
            {
                primaryTask = request.PrimaryTask,
                usedSections = request.UsedSections,
                mostUsefulFeature = request.MostUsefulFeature,
                clarityScore = request.ClarityScore,
                missingForDecision = request.MissingForDecision,
                continueUsingAnswer = request.ContinueUsingAnswer,
                improvementPriority = NormalizeOptionalText(request.ImprovementPriority)
            }),
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

        if (request.ClarityScore is < 1 or > 5)
        {
            ModelState.AddModelError(nameof(request.ClarityScore), "Оцените понятность данных от 1 до 5.");
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
    public int ClarityScore { get; set; }

    [Required]
    public List<string> MissingForDecision { get; set; } = new();

    [Required]
    public string? GeneralComment { get; set; }

    [Required]
    public string? ContinueUsingAnswer { get; set; }

    public string? ImprovementPriority { get; set; }
}

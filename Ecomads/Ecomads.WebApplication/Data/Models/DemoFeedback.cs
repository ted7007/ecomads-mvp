using System.ComponentModel.DataAnnotations;

namespace Ecomads.WebApplication.Data.Models;

public class DemoFeedback
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [Required]
    public string GeneralComment { get; set; } = null!;

    public int DashboardClarityScore { get; set; }

    public int RecommendationsUsefulnessScore { get; set; }

    public string? WrongOrQuestionableRecommendations { get; set; }

    [Required]
    public string MostUsefulFeature { get; set; } = null!;

    public string? MissingForRegularUsage { get; set; }

    [Required]
    public string ContinueTestingAnswer { get; set; } = null!;

    [Required]
    public string WillingToPayAnswer { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
}

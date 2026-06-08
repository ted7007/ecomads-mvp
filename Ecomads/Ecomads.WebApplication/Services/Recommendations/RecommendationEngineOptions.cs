namespace Ecomads.WebApplication.Services.Recommendations;

public sealed class RecommendationEngineOptions
{
    public decimal TargetDrr { get; set; } = 30m;
    public int MinClicksForConclusion { get; set; } = 30;
    public decimal MinSpendForConclusion { get; set; } = 500m;
    public int MinOrdersForPositiveConclusion { get; set; } = 3;
    public int MinViewsForCtrConclusion { get; set; } = 1000;
    public int MaxInsightsForLlm { get; set; } = 20;
    public double PriorityMultiplier { get; set; } = 25d;
}

using Ecomads.WebApplication.Services.Recommendations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public class RecommendationReportStatusTests
{
    private static RuleEvaluation Rule(string status, string reason = "condition_not_met", bool enabled = true) =>
        new("R02", 3, 1, enabled, status, reason, "", null);

    [Theory]
    [InlineData(false, false, "not_triggered", "budget_sufficient", "no_deviations")]
    [InlineData(false, false, "not_triggered", "campaign_not_eligible", "no_deviations")]
    [InlineData(false, true, "not_triggered", "budget_sufficient", "empty")]
    [InlineData(false, false, "skipped", "budget_incomplete", "not_ready")]
    [InlineData(false, true, "skipped", "budget_incomplete", "not_ready")]
    [InlineData(false, false, "triggered", "budget_below_day", "preliminary")]
    [InlineData(true, false, "not_triggered", "condition_not_met", "no_deviations")]
    [InlineData(true, false, "not_triggered", "low_sample", "low_sample")]
    [InlineData(true, false, "skipped", "unit_price_unavailable", "limited")]
    public void StatusDescribesChecksRatherThanPresenceOfClusterRows(bool clusters, bool empty, string status, string reason, string expected)
    {
        Assert.Equal(expected, RecommendationService.ReportStatus(clusters, empty, [Rule(status, reason)]));
    }

    [Theory]
    [InlineData(false, "not_ready")]
    [InlineData(true, "unavailable")]
    public void NoChecksCannotClaimNoDeviations(bool clusters, string expected)
    {
        Assert.Equal(expected, RecommendationService.ReportStatus(clusters, false, []));
        Assert.Equal(expected, RecommendationService.ReportStatus(clusters, false, [Rule("not_triggered", enabled: false)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnavailableRuleIsNotHiddenByAnotherCompletedCheck(bool clusters)
    {
        Assert.Equal("limited", RecommendationService.ReportStatus(clusters, false, [Rule("not_triggered"), Rule("skipped")]));
        Assert.Equal("limited", RecommendationService.ReportStatus(clusters, false, [Rule("triggered"), Rule("skipped")]));
    }
}

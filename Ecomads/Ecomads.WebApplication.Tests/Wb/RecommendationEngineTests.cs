using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class RecommendationEngineTests
{
    private static readonly RecommendationOptions Options = new();
    private static readonly RecommendationNorm Norm = new(0.20m, "default", 0, 0, 0);
    private static readonly RecommendationQuality Ready = new(true, true, true, true);
    private static RecommendationFacts Facts(decimal spend, int? units, decimal? price = 2000m) =>
        new(spend, null, 10, null, 999, units, price);

    [Theory]
    [InlineData(790, "not_triggered")]
    [InlineData(800, "triggered")]
    [InlineData(810, "triggered")]
    public void NoUnitsUsesMoneyThresholdAndUnitsRegardlessOfOrdersOrClicks(decimal spend, string expected)
    {
        var facts = Facts(spend, 0) with { Clicks = null };
        var result = RecommendationEngine.Evaluate(facts, Norm, Ready, Options);
        Assert.Equal(expected, result[0].Status);
        Assert.Equal(800m, result[0].Threshold);
        Assert.Equal("not_triggered", result[1].Status);
    }

    [Theory]
    [InlineData(299, "not_triggered")]
    [InlineData(300, "triggered")]
    public void CheapProductsStillHaveConfiguredSpendFloor(decimal spend, string status) =>
        Assert.Equal(status, RecommendationEngine.Evaluate(Facts(spend, 0, 100), Norm, Ready, Options)[0].Status);

    [Fact]
    public void HighDrrRequiresBothPriceScenarioAndPlusOneUnit()
    {
        var strong = RecommendationEngine.Evaluate(Facts(500, 3, 500), Norm, Ready, Options);
        Assert.Equal("triggered", strong[1].Status);
        var borderline = RecommendationEngine.Evaluate(Facts(360, 3, 500), Norm, Ready, Options);
        Assert.Equal("not_triggered", borderline[1].Status); // Dmin = N, plus-one = 18%.
        var failsPlusOne = RecommendationEngine.Evaluate(Facts(390, 3, 500), Norm, Ready, Options);
        Assert.Equal("not_triggered", failsPlusOne[1].Status); // Dmin > N, plus-one < N.
    }

    [Fact]
    public void EqualityAndRoundingDoNotChangePredicates()
    {
        var equality = RecommendationEngine.Evaluate(Facts(400, 3, 500), Norm, Ready, Options);
        Assert.Equal("not_triggered", equality[1].Status); // plus-one exactly 20%.
        var justAbove = RecommendationEngine.Evaluate(Facts(400.00001m, 3, 500), Norm, Ready, Options);
        Assert.Equal("triggered", justAbove[1].Status);
    }

    [Fact]
    public void ScenarioIsNotComputedFromRoundedPercentage()
    {
        var economy = RecommendationEngine.Economy(Facts(600, 4, 500), Norm, Options.PriceScenario);
        Assert.Equal(2000m, economy.EstimatedOrderSum);
        Assert.Equal(0.30m, economy.Drr);
        Assert.Equal(0.25m, economy.DrrMin);
        Assert.Equal(0.375m, economy.DrrMax);
    }

    [Fact]
    public void ZeroUnitsHasZeroEstimatedSumAndNullDrr()
    {
        var economy = RecommendationEngine.Economy(Facts(800, 0), Norm, Options.PriceScenario);
        Assert.Equal(0m, economy.EstimatedOrderSum);
        Assert.Null(economy.Drr);
        Assert.Null(economy.DrrMin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void MissingPriceNeverFallsBackToTheSpendFloor(int? price) =>
        Assert.All(RecommendationEngine.Evaluate(Facts(1000, 0, price), Norm, Ready, Options),
            rule => { Assert.Equal("skipped", rule.Status); Assert.Equal("unit_price_unavailable", rule.ReasonCode); });

    [Fact]
    public void MissingUnitsNeverBecomeOrdersOrZero() =>
        Assert.All(RecommendationEngine.Evaluate(Facts(1000, null), Norm, Ready, Options),
            rule => { Assert.Equal("skipped", rule.Status); Assert.Equal("units_missing", rule.ReasonCode); });

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PartialSourcesBlockTheirEconomicRules(bool fullstats, bool clusters) =>
        Assert.All(RecommendationEngine.Evaluate(Facts(1000, 0), Norm, Ready with
            { FullStatsComplete = fullstats, ClustersComplete = clusters }, Options),
            rule => { Assert.Equal("skipped", rule.Status); Assert.Equal("incomplete_data", rule.ReasonCode); });

    [Fact]
    public void UnverifiedMaturityPermitsInternalDiagnosticsButUnverifiedFieldsOrSourcesDoNot()
    {
        Assert.Equal("triggered", RecommendationEngine.Evaluate(Facts(1000, 0), Norm, Ready, Options)[0].Status);
        Assert.Equal("fields_unverified", RecommendationEngine.Evaluate(Facts(1000, 0), Norm,
            Ready with { FieldsVerified = false }, Options)[0].ReasonCode);
        Assert.Equal("sources_unverified", RecommendationEngine.Evaluate(Facts(1000, 0), Norm,
            Ready with { SourcesComparable = false }, Options)[0].ReasonCode);
    }

    [Fact]
    public void DisabledRuleDoesNotBlockAnotherRule()
    {
        var result = RecommendationEngine.Evaluate(Facts(500, 3, 500), Norm, Ready,
            new RecommendationOptions { Rule03Enabled = false });
        Assert.Equal("disabled", result[0].Status);
        Assert.Equal("triggered", result[1].Status);
    }

    [Fact]
    public void NormInheritancePreservesExplicitZeroAndExistingThirtyPercent()
    {
        Assert.Equal("default", RecommendationEngine.ResolveNorm(null, null, null).Source);
        Assert.Equal(0.20m, RecommendationEngine.ResolveNorm(null, null, null).Value);
        Assert.Equal(0.30m, RecommendationEngine.ResolveNorm(null, null, 30).Value);
        Assert.Equal("campaign", RecommendationEngine.ResolveNorm(null, 25, 30).Source);
        Assert.Equal("article", RecommendationEngine.ResolveNorm(0, 25, 30).Source);
        Assert.Equal(0m, RecommendationEngine.ResolveNorm(0, 25, 30).Value);
    }

    [Fact]
    public void CoverageUnionsIntervalsOnlyForTheExactPair()
    {
        var start = new DateOnly(2026, 9, 27);
        var window = new RecommendationWindow(start, start.AddDays(13));
        ClusterCoverageInterval[] intervals = [new(start, start.AddDays(6), [new(123, 1)]),
            new(start.AddDays(7), start.AddDays(13), [new(123, 1), new(123, 2)])];
        Assert.True(RecommendationCoverage.Covers(window, 123, 1, intervals));
        Assert.False(RecommendationCoverage.Covers(window, 123, 2, intervals));
        Assert.False(RecommendationCoverage.Covers(window, 456, 1, intervals));
        Assert.False(RecommendationCoverage.Covers(new(start, start.AddDays(14)), 123, 1, intervals));
    }

    [Fact]
    public void RecommendationsAreEnabledByDefaultWithoutAStoreAllowlist()
    {
        Assert.True(new RecommendationOptions().Enabled);
        Assert.True(new RecommendationOptions().SourcesComparable);
        Assert.False(new RecommendationOptions { Enabled = false }.Enabled);
    }
}

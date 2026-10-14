using Ecomads.WebApplication.Services.Recommendations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class RecommendationDiagnosticsTests
{
    private static readonly RecommendationNorm Norm = new(.2m, "default", 0, 0, 0);
    private static RecommendationFacts F(decimal s = 1000, int v = 1000, int c = 100, int a = 20, int o = 3, decimal p = 1000) => new(s, v, c, a, o, o, p);
    private static IReadOnlyList<RecommendationDiagnostic> Calculate(RecommendationFacts current, RecommendationFacts[]? peers = null, RecommendationFacts? previous = null,
        DiagnosticThresholds? thresholds = null) => RecommendationDiagnostics.Calculate(current, RecommendationEngine.Economy(current, Norm, .2m), Norm,
            peers ?? [], previous, previous == null ? null : RecommendationEngine.Economy(previous, Norm, .2m), thresholds ?? new());
    [Fact]
    public void NoNormOrThreeOtherPeersMeansNoFunnelSignal()
    {
        Assert.Empty(Calculate(F(a: 0, o: 0)));
        Assert.Empty(Calculate(F(a: 0, o: 0), [F(), F()]));
        Assert.Contains(Calculate(F(a: 0, o: 0), [F(), F(), F()]), d => d.Code == "F02");
    }
    [Fact]
    public void PeerNormIsRatioOfSumsAndNotMeanOfRatios()
    {
        // mean ratios=67%, aggregate=21%; current 20% must not signal below 70% of 21%.
        var signals = Calculate(F(c: 100, a: 20), [F(c: 30, a: 30), F(c: 30, a: 30), F(c: 1000, a: 160)]);
        Assert.DoesNotContain(signals, d => d.Code == "F02");
    }
    [Fact]
    public void EconomicsAndSpendTakePriorityAndAtMostTwoExplanations()
    {
        var signals = Calculate(F(s: 3000, c: 100, a: 0, o: 3), [F(), F(), F()], F(s: 1000, c: 100, o: 3));
        Assert.Equal(new[] { "D01", "D02" }, signals.Select(d => d.Code));
    }
    [Fact]
    public void ZeroBaselineAndMissingPreviousDoNotGenerateInfiniteGrowth()
    {
        Assert.Empty(Calculate(F(), previous: F(s: 0, c: 0, a: 0, o: 0)));
        Assert.Empty(Calculate(F(), previous: null));
    }
    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    public void FunnelMinimumIsIndependentAndInclusive(int clicks, bool expected) =>
        Assert.Equal(expected, Calculate(F(c: clicks, a: 0), thresholds: new(CartConversion: .2m)).Any(d => d.Code == "F02"));
}

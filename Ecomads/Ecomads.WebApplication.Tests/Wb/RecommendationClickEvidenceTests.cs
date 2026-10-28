using System.Text.Json;
using Ecomads.WebApplication.Services.Recommendations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public class RecommendationClickEvidenceTests
{
    private static readonly RecommendationWindow Window = new(new(2026, 9, 13), new(2026, 10, 12));

    [Theory]
    [InlineData(1000, 77, true, 38, "actual", "insufficient", 39)]
    [InlineData(1000, 77, true, 39, "actual", "sufficient", 39)]
    [InlineData(99, 77, true, 149, "fallback", "insufficient", 150)]
    [InlineData(99, 77, true, 150, "fallback", "sufficient", 150)]
    [InlineData(100, 10, true, 30, "actual", "sufficient", 30)]
    [InlineData(1441, 77, true, 56, "actual", "insufficient", 57)]
    [InlineData(1441, 77, true, 57, "actual", "sufficient", 57)]
    [InlineData(1441, 77, true, 86, "actual", "sufficient", 57)]
    [InlineData(1000, 77, false, 39, "fallback", "insufficient", 150)]
    public void Boundaries(int clicks, int units, bool complete, int clusterClicks, string source, string result, int minimum)
    {
        var e = RecommendationClickEvidence.Calculate(Window, clicks, units, complete, clusterClicks);
        Assert.Equal(source, e.Source); Assert.Equal(result, e.Result); Assert.Equal((decimal)minimum, e.MinimumClicks);
    }

    [Fact]
    public void NapkinWithEightySixClicksIsExcludedAtMeasuredProductConversion()
    {
        var facts = new RecommendationFacts(600, 1000, 86, 0, 0, 0, 1425);
        var norm = new RecommendationNorm(.2m, "default", 0, 0, 0);
        var evidence = RecommendationClickEvidence.Calculate(Window, 1441, 77, true, 86);
        var presentation = RecommendationActions.Select(new("R03", 2, 2, true, "triggered", "", "", 570),
            facts, norm, new(true, true, true, true), RecommendationEngine.Economy(facts, norm, .2m),
            new("ready", "unified", "cpm", false), 77, null, Window, new(2026, 10, 14), evidence);
        Assert.Equal(57m, evidence.MinimumClicks);
        Assert.Equal("exclude", presentation.Primary.Kind);
    }

    [Fact]
    public void ZeroConversionAndMissingClicksCannotExclude()
    {
        var zero = RecommendationClickEvidence.Calculate(Window, 100, 0, true, 1000);
        Assert.Null(zero.MinimumClicks); Assert.Equal("insufficient", zero.Result);
        Assert.Equal("unknown", RecommendationClickEvidence.Calculate(Window, 100, 10, true, null).Result);
    }

    [Theory]
    [InlineData(569, 0, "ready", false, "watch")]
    [InlineData(570, 0, "ready", false, "exclude")]
    [InlineData(600, 1, "ready", false, "watch")]
    [InlineData(600, 0, "stale", false, "watch")]
    [InlineData(600, 0, "ready", true, "watch")]
    public void ClicksDoNotBypassOtherGuards(decimal spend, int units, string status, bool excluded, string expected)
    {
        var f = new RecommendationFacts(spend, 1000, 150, 0, 0, units, 1425);
        var n = new RecommendationNorm(.2m, "default", 0, 0, 0);
        var p = RecommendationActions.Select(new("R03", 2, 2, true, "triggered", "", "", 570), f, n,
            new(true, true, true, true), RecommendationEngine.Economy(f, n, .2m),
            new(status, "manual", "cpm", excluded, 100, 10, 1), 77, null, Window, new(2026, 10, 14));
        Assert.Equal(expected, p.Primary.Kind);
        if (expected == "watch") Assert.Empty(p.Alternatives);
    }

    [Theory]
    [InlineData("manual", 25, "reduce_bid")]
    [InlineData("unified", 25, "watch")]
    [InlineData("unified", 39, "exclude")]
    [InlineData(null, 39, "watch")]
    [InlineData("manual", null, "watch")]
    public void ExpertExample(string? bidType, int? clicks, string expected)
    {
        var facts = new RecommendationFacts(600, 1000, clicks, 0, 0, 0, 1425);
        var norm = new RecommendationNorm(.2m, "default", 0, 0, 0);
        var evidence = RecommendationClickEvidence.Calculate(Window, 1000, 77, true, clicks);
        var p = RecommendationActions.Select(new("R03", 2, 2, true, "triggered", "", "", 570),
            facts, norm, new(true, true, true, true), RecommendationEngine.Economy(facts, norm, .2m),
            new("ready", bidType, "cpm", false, 100, 10, 1), 77, null, Window, new(2026, 10, 14), evidence);
        Assert.Equal(expected, p.Primary.Kind);
        if (expected != "exclude") Assert.DoesNotContain(p.Alternatives, a => a.Kind == "exclude");
        Assert.Equal(evidence, p.ClickEvidence);
        Assert.Equal(p, JsonSerializer.Deserialize<RecommendationPresentation>(JsonSerializer.Serialize(p))! with
        { Alternatives = p.Alternatives, Restrictions = p.Restrictions, Diagnostics = p.Diagnostics });
    }
}

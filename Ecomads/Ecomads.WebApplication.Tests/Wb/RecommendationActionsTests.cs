using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Data.Models;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class RecommendationActionsTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly RecommendationWindow Window = new(new(2026, 10, 1), new(2026, 10, 7));
    private static readonly RecommendationNorm Norm = new(.2m, "default", 0, 0, 0);
    private static readonly RecommendationQuality Ready = new(true, true, true, true);
    private static readonly AdvertisingControl Unified = new("ready", "unified", "cpm", false);
    private static readonly AdvertisingControl Manual = new("ready", "manual", "cpm", false, 100, 10, 1);
    private static RecommendationFacts Facts(int q = 3, decimal spend = 1000) => new(spend, 1000, 150, 20, 3, q, 1000);
    private static RecommendationPresentation Select(string rule = "R06", int? total = 100, AdvertisingControl? control = null,
        RecommendationFacts? facts = null, RecommendationWindow? window = null, DateOnly? launch = null)
    {
        var f = facts ?? Facts();
        return RecommendationActions.Select(new(rule, 1, 3, true, "triggered", "test", "", .2m), f, Norm, Ready,
            RecommendationEngine.Economy(f, Norm, .2m), control ?? Unified, total, launch, window ?? Window, Today);
    }
    [Theory]
    [InlineData(1199, "watch")]
    [InlineData(1200, "exclude")]
    [InlineData(1201, "exclude")]
    public void UnifiedUsesInclusiveDoubleNorm(decimal spend, string action) =>
        Assert.Equal(action, Select(facts: Facts(spend: spend)).Primary.Kind);

    [Theory]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(0, false)]
    [InlineData(null, false)]
    public void KeyShareBoundaryAndUnknownDenominator(int? total, bool key)
    {
        var result = Select(total: total, facts: Facts(spend: 1500));
        Assert.Equal(key, result.IsKey);
        if (key || total is null or 0) Assert.NotEqual("exclude", result.Primary.Kind);
    }
    [Fact]
    public void KnownZeroUnitsCanExcludeWhileUnknownUnitsCannot()
    {
        Assert.Equal("exclude", Select("R03", 0, facts: Facts(0)).Primary.Kind);
        Assert.Equal(0m, Select("R03", 0, facts: Facts(0)).KeyShare);
        Assert.Equal("exclude", Select("R03", null, facts: Facts(0)).Primary.Kind);
    }
    [Fact]
    public void ManualDecreaseIsProportionalCappedAndRoundedUp()
    {
        var twenty = Select(control: Manual, facts: Facts(spend: 750)).Primary;
        Assert.Equal(20m, twenty.ReductionPercent); Assert.Equal(80m, twenty.TargetBid);
        var capped = Select(control: Manual, facts: Facts(spend: 1500)).Primary;
        Assert.Equal(30m, capped.ReductionPercent); Assert.Equal(70m, capped.TargetBid);
        Assert.Equal(75m, Select(control: Manual with { MinimumBid = 74, BidStep = 5 }).Primary.TargetBid);
        Assert.Null(Select(control: Manual with { BidStep = null }).Primary.TargetBid);
        Assert.Null(Select(control: Manual with { CurrentBid = null }).Primary.TargetBid);
        Assert.Equal("watch", Select(control: Manual with { MinimumBid = 100 }).Primary.Kind);
    }
    [Fact]
    public void KeyClustersNeverExcludeEvenForR03()
    {
        Assert.Equal("check_product", Select(total: 15, facts: Facts(spend: 1500)).Primary.Kind);
        Assert.Equal("reduce_bid", Select(total: 15, control: Manual).Primary.Kind);
        Assert.Equal("check_product", Select(total: 15, control: Manual with { Status = "source_error" }).Primary.Kind);
        Assert.Equal("watch", Select("R03", 15).Primary.Kind);
    }
    [Fact]
    public void UnknownStaleAndExcludedAreDistinct()
    {
        foreach (var control in new[] { Unified with { Status = "unknown" }, Unified with { Status = "stale" }, Unified with { Excluded = true } })
            Assert.Equal("watch", Select("R03", control: control).Primary.Kind);
        Assert.Contains(Select(control: Unified with { Excluded = true }).Restrictions, x => x.Code == "excluded");
        Assert.Contains(Select(control: Unified with { Status = "stale" }).Restrictions, x => x.Code == "settings_stale");
    }
    [Fact]
    public void RecentPeriodHasDiagnosticButNoActionAndDMinusTwoAllowsAction()
    {
        Assert.Equal("watch", Select("R03", facts: Facts(0), window: new(Today.AddDays(-1), Today.AddDays(-1))).Primary.Kind);
        Assert.Equal("exclude", Select("R03", facts: Facts(0), window: new(Today.AddDays(-2), Today.AddDays(-2))).Primary.Kind);
    }
    [Theory]
    [InlineData(0, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void ProtectionHasExactlyTwentyOneDays(int elapsed, bool protectedNow)
    {
        var launch = Today.AddDays(-elapsed);
        Assert.Equal(protectedNow, RecommendationActions.LaunchProtected(launch, new(Today, Today), Today));
    }
    [Fact]
    public void HistoricalOverlapAlsoProtectedAndUnknownLaunchNotInferred()
    {
        Assert.True(RecommendationActions.LaunchProtected(new(2026, 9, 20), Window, Today));
        Assert.False(RecommendationActions.LaunchProtected(null, Window, Today));
        Assert.False(RecommendationActions.LaunchProtected(new(2026, 9, 1), Window, Today));
    }
    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    public void PackagesRequireFiveUniqueR03Exclusions(int count, int packages)
    {
        var campaign = Guid.NewGuid(); var article = Guid.NewGuid();
        var cards = Enumerable.Range(0, count).Select(i => new RecommendationCard(Guid.NewGuid(),
            new(Guid.NewGuid(), campaign, article, "1", "p", i.ToString()), new("R03", 1, 2, true, "triggered", "", "", 300),
            Facts(0), Norm, RecommendationEngine.Economy(Facts(0), Norm, .2m), false,
            Select("R03", 0, facts: Facts(0)))).ToArray();
        var result = RecommendationActions.Packages(cards.Concat(cards));
        Assert.Equal(packages, result.Count);
        if (packages > 0) { Assert.Equal(count, result[0].EvaluationIds.Count); Assert.Equal(count * 1000m, result[0].WeeklySpend); }
    }
    [Theory]
    [InlineData("unknown", null)]
    [InlineData("stale", "manual")]
    [InlineData("ready", "manual")]
    public void PartialDataNeverAllowsExclusionOrBidAlternative(string status, string? bidType)
    {
        var f = Facts(0);
        var q = new RecommendationQuality(false, false, true, true, AvailableDays: 13, ExpectedDays: 14);
        var p = RecommendationActions.Select(new("R03", 1, 2, true, "triggered", "", "", 300), f, Norm, q,
            RecommendationEngine.Economy(f, Norm, .2m), new(status, bidType, "cpm"), null, null,
            new(new(2026, 9, 29), new(2026, 10, 12)), Today);
        Assert.Equal("watch", p.Primary.Kind);
        Assert.Equal(1000m / 13 * 7, p.WeeklySpend);
        Assert.Empty(p.Alternatives);
    }

    [Fact]
    public void ImportedArticleBidAppliesWithoutClusterOverrideAndCpcIsExplicitlyUnsupported()
    {
        var now = DateTime.UtcNow;
        var campaign = new Ecomads.WebApplication.Services.Wb.WbCampaignInfo(123, "C", 9, "cpm", [1, 2],
            BidType: "manual", ArticleBids: [new(1, 125m, null), new(2, 130m, null)]);
        var payload = new AdvertisingSettingsPayload([], [new("1", [])], campaign, now);
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var row = new WbAdvertisingSettings { Status = "ready", BidType = "manual", PaymentType = "cpm",
            ReceivedAtUtc = now, PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload, json) };
        var control = AdvertisingSettingsPayload.Control(row, "1", "cluster", now);
        Assert.Equal(125m, control.CurrentBid); Assert.False(control.Excluded);
        Assert.Equal("ready", control.Status);
        Assert.Equal("unknown", AdvertisingSettingsPayload.Control(row, "2", "cluster", now).Status);
        row.PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload with { Bids = [new("1", "cluster", 150m)] }, json);
        Assert.Equal(150m, AdvertisingSettingsPayload.Control(row, "1", "cluster", now).CurrentBid);
        row.Status = "source_error";
        row.PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload with { Campaign = campaign with { PaymentType = "cpc" } }, json);
        control = AdvertisingSettingsPayload.Control(row, "1", "cluster", now);
        Assert.Equal("unsupported", control.Status); Assert.Equal("cpc", control.PaymentType); Assert.Equal("manual", control.BidType);
        Assert.Null(control.Excluded);
    }

    [Fact]
    public void SettingsSourceNeverTurnsMissingExclusionsIntoFalse()
    {
        var row = new WbAdvertisingSettings { Status = "ready", ReceivedAtUtc = DateTime.UtcNow, PayloadJson = "{\"bids\":[],\"minus\":[]}" };
        Assert.Null(AdvertisingSettingsPayload.Control(row, "1", "a", DateTime.UtcNow).Excluded);
        row.PayloadJson = "{}";
        Assert.Equal("invalid_source", AdvertisingSettingsPayload.Control(row, "1", "a", DateTime.UtcNow).Status);
        row.ReceivedAtUtc = DateTime.UtcNow.AddHours(-25);
        Assert.Equal("stale", AdvertisingSettingsPayload.Control(row, "1", "a", DateTime.UtcNow).Status);
    }
}

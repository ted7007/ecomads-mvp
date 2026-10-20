using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public class ResultCalculationTests
{
    private static readonly DateTime At = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    public static ResultMember Member(string rule = "R03", string action = "exclude", string cluster = "test")
    {
        var card = new RecommendationCard(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1", "Product", cluster),
            new(rule, 2, 2, true, "triggered", "", "", 300), new(2000, 100, 40, 0, 0, 0, 1000), new(.2m, "store", 1, 0, 0),
            new(0, null, null, null, null, 200), false,
            new(new(action, "action", 30), [], null, false, 2000, [], [], Details: new("budget", new(new(2026, 9, 1), new(2026, 9, 7)), [], TopUp: 1000)));
        return new(Guid.NewGuid(), Guid.NewGuid(), card, new("before", At, "settings", "ready", false, [100]), new("after", At.AddDays(1), "settings", "ready", true, [70]));
    }
    public static ResultMetrics Metrics(decimal spend = 10000, decimal objectSpend = 2000, int totalUnits = 100, int units = 10) =>
        new(spend, 50000, 10000, [new("1:test", "1", objectSpend, 40, units, 20000, totalUnits, 1000, 7, 0, 0)], [], []);

    [Fact]
    public void ExclusionCountsOnlyActualCampaignReduction()
    {
        var result = ResultCalculation.Calculate([Member()], Metrics(), Metrics(9500, 0), []);
        Assert.Equal("confirmed", result.Status); Assert.Equal(500m, result.Savings);
        Assert.Equal(2000m, ResultCalculation.Calculate([Member()], Metrics(), Metrics(1000, 0), []).Savings);
    }
    [Fact]
    public void NoSavingsCanBeRedistributionButNeverMoney()
    {
        var before = Metrics(); var after = Metrics() with { CampaignRevenue = 100000 };
        var r = ResultCalculation.Calculate([Member()], before, after, []);
        Assert.Equal("redistributed", r.Status); Assert.Null(r.Savings);
        Assert.Equal("no_effect", ResultCalculation.Calculate([Member()], before, before, []).Status);
    }
    [Theory]
    [InlineData(85, "confirmed")]
    [InlineData(84, "not_confirmed")]
    public void TotalUnitsBoundary(int units, string status) =>
        Assert.Equal(status, ResultCalculation.Calculate([Member()], Metrics(), Metrics(9500, 0, units), []).Status);

    [Theory]
    [InlineData(7, "confirmed")]
    [InlineData(6, "not_confirmed")]
    public void ReducedBidProtectsAdUnits(int units, string status) =>
        Assert.Equal(status, ResultCalculation.Calculate([Member("R06", "reduce_bid")], Metrics(), Metrics(9500, 1000, 85, units), []).Status);

    [Theory]
    [InlineData(1100, 12000, "confirmed")]
    [InlineData(1101, 12000, "not_evaluated")]
    [InlineData(1100, 12001, "not_evaluated")]
    public void PriceAndOtherCampaignBoundaries(decimal price, decimal spend, string status)
    {
        var after = Metrics(9500, 0) with { OtherCampaignSpend = spend };
        after = after with { Objects = [after.Objects[0] with { UnitPrice = price }] };
        Assert.Equal(status, ResultCalculation.Calculate([Member()], Metrics(), after, []).Status);
    }
    [Fact]
    public void UnknownAndZeroDataDoNotManufactureSavings()
    {
        var after = Metrics(9500, 0);
        foreach (var bad in new[] { after with { Gaps = ["missing day"] }, after with { Objects = [after.Objects[0] with { TotalUnits = null }] },
            after with { Objects = [after.Objects[0] with { KnownStockDays = 6 }] }, after with { Objects = [after.Objects[0] with { ZeroStockDays = 1 }] } })
            Assert.Equal("not_evaluated", ResultCalculation.Calculate([Member()], Metrics(), bad, []).Status);
        Assert.Equal("not_evaluated", ResultCalculation.Calculate([Member()], Metrics(), after, ["Постороннее изменение ставки"]).Status);
        Assert.False(ResultCalculation.FellMore(0, 0, .15m));
        Assert.True(ResultCalculation.ChangedMore(0, 1, .2m));
    }
    [Fact]
    public void R01IncludesAvoidedExpenseAndR02HasNoMoney()
    {
        var after = Metrics(0, 0); after = after with { Objects = [after.Objects[0] with { ZeroStockDays = 7, PausedZeroDays = 3 }] };
        var r = ResultCalculation.Calculate([Member("R01", "pause_campaign")], Metrics(10000, 700), after, []);
        Assert.Equal("confirmed", r.Status); Assert.Equal(300m, r.Savings);
        var budget = ResultCalculation.Calculate([Member("R02", "top_up_budget")], Metrics(), after, []);
        Assert.Equal("done", budget.Status); Assert.Null(budget.Savings);
    }
    [Fact]
    public void MixedAndUnspecifiedActionsRemainWithoutMoney()
    {
        Assert.Equal("not_evaluated", ResultCalculation.Calculate([Member(), Member("R06", "reduce_bid")], Metrics(), Metrics(9500), []).Status);
        Assert.Equal("not_evaluated", ResultCalculation.Calculate([Member("R03", "reduce_bid")], Metrics(), Metrics(9500), []).Status);
    }
    [Theory]
    [InlineData(86, false)]
    [InlineData(85, true)]
    [InlineData(70, true)]
    public void ApplicationUsesHalfRecommendedReduction(decimal bid, bool expected)
    {
        var member = Member("R06", "reduce_bid");
        Assert.Equal(expected, ResultObservationReader.Applied(member.Card, member.Before, member.Applied with { Bids = [bid] }));
        Assert.False(ResultObservationReader.Applied(member.Card, member.Before, member.Applied with { State = "source_error", Bids = [70] }));
    }
    [Fact]
    public void BudgetAndPlacementEvidenceMustBeKnown()
    {
        var member = Member("R02", "top_up_budget");
        Assert.False(ResultObservationReader.Applied(member.Card, member.Before with { Budget = null }, member.Applied with { Budget = 2000 }));
        Assert.False(ResultObservationReader.Applied(member.Card, member.Before with { Budget = 1000 }, member.Applied with { Budget = 1499 }));
        Assert.True(ResultObservationReader.Applied(member.Card, member.Before with { Budget = 1000 }, member.Applied with { Budget = 1500 }));
        var reduction = Member("R07", "reduce_article_bid");
        Assert.False(ResultObservationReader.Applied(reduction.Card, reduction.Before with { Bids = [100, -1] }, reduction.Applied with { Bids = [-1, 70] }));
    }
    [Fact]
    public void UpdatedActionIsAVersionOfTheSameRecommendation()
    {
        var card = Member().Card;
        var other = card with { Presentation = card.Presentation! with { Primary = new("reduce_bid", "Снизьте ставку", 30) } };
        Assert.Equal(ResultsService.Key(card), ResultsService.Key(other));
    }

    [Fact]
    public void ForecastExpiresAndNeverEntersMeasuredSavings()
    {
        var m = new WbResultMeasurement { Status = "confirmed", DetectedDate = new(2026, 10, 1), Savings = 700,
            ActionPreserved = true, ContinuationCheckedAtUtc = At.AddDays(10), MembersJson = System.Text.Json.JsonSerializer.Serialize(new[] { Member() }, ResultsService.Json) };
        Assert.Equal(2000m, ResultsReportReader.Forecast(m, At.AddDays(10)));
        Assert.Null(ResultsReportReader.Forecast(m, At.AddDays(12)));
        Assert.Equal(700, m.Savings);
        m.ActionPreserved = false; Assert.Null(ResultsReportReader.Forecast(m, At.AddDays(10)));
    }
}

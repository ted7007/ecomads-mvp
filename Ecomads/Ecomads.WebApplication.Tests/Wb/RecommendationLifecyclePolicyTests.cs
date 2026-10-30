using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public class RecommendationLifecyclePolicyTests
{
    private static RecommendationCard Card() => new(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "123", "Product", " Бумажные   Салфетки "),
        new("R03", 2, 2, true, "triggered", "", "", 300), new(1000, 100, 10, 0, 0, 0, 100),
        new(.2m, "store", 1, 0, 0), new(0, null, null, null, null, 20), false,
        new(new("exclude", "Исключить"), [], 0, false, 500, [], []));

    [Fact]
    public void IdentityIgnoresCalculationIdsAndHarmlessNameChangesButSeparatesProductsAndRules()
    {
        var card = Card();
        Assert.Equal(RecommendationLifecycle.Identity(card), RecommendationLifecycle.Identity(card with {
            EvaluationId = Guid.NewGuid(), Subject = card.Subject with { ArticleId = Guid.NewGuid(), ClusterName = "бумажные салфетки" } }));
        Assert.NotEqual(RecommendationLifecycle.Identity(card), RecommendationLifecycle.Identity(card with { Subject = card.Subject with { NomenclatureId = "456" } }));
        Assert.NotEqual(RecommendationLifecycle.Identity(card), RecommendationLifecycle.Identity(card with { Rule = card.Rule with { RuleId = "R06" } }));
    }

    [Fact]
    public void RefusalDoesNotExpireForZeroUnknownPreliminaryOrIncomparableEstimates()
    {
        var card = Card(); var at = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var refusal = new WbSnapshotDecision { Status = "irrelevant", CreatedAtUtc = at };
        var doubled = card with { Presentation = card.Presentation! with { WeeklySpend = 1000 } };
        Assert.True(RecommendationLifecycle.RefusalExpired(refusal, card, doubled, at.AddDays(14)));
        Assert.False(RecommendationLifecycle.RefusalExpired(refusal, card, doubled, at.AddDays(13)));
        Assert.False(RecommendationLifecycle.RefusalExpired(refusal, card, doubled, at.AddDays(14).AddMinutes(-1)));
        Assert.False(RecommendationLifecycle.RefusalExpired(refusal, card, doubled with { Preliminary = true }, at.AddDays(20)));
        Assert.False(RecommendationLifecycle.RefusalExpired(refusal, card, doubled with { Norm = card.Norm with { Value = .3m } }, at.AddDays(20)));
        Assert.False(RecommendationLifecycle.RefusalExpired(refusal, card, doubled with { Rule = card.Rule with { RuleVersion = 3 } }, at.AddDays(20)));
        Assert.False(RecommendationLifecycle.RefusalExpired(refusal, card with { Presentation = card.Presentation! with { WeeklySpend = 0 } }, doubled, at.AddDays(20)));
        Assert.False(RecommendationLifecycle.RefusalExpired(refusal, card with { Rule = card.Rule with { RuleId = "R02" } }, doubled, at.AddDays(20)));
    }
}

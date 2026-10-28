using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class RecommendationRankingTests
{
    private static readonly Guid Campaign = Guid.NewGuid(), Article = Guid.NewGuid(), Run = Guid.NewGuid();
    private static readonly RecommendationWindow Window = new(new(2026, 10, 1), new(2026, 10, 14));
    private static readonly RecommendationNorm Norm = new(.2m, "default", 0, 0, 0);

    private static RecommendationCard Card(string rule, decimal weekly, decimal? drr = null, string action = "exclude",
        string scope = "cluster", string cluster = "a", int priority = 4, bool preliminary = false) =>
        new(Guid.NewGuid(), new(Guid.NewGuid(), Campaign, Article, "1", "Product", scope == "cluster" ? cluster : null, scope),
            new(rule, 1, rule == "R03" ? 2 : 3, true, "triggered", "test", "", 300),
            new(weekly * 2, 1000, 150, 0, 3, rule == "R03" ? 0 : 3, 1000), Norm,
            new(10000, drr, null, null, null, 200), preliminary,
            new(new(action, action, action.StartsWith("reduce_") ? 30 : null), [], null, false, weekly, [], [], Priority: priority));

    private static RecommendationResponse Report(params RecommendationCard[] cards) => new(true, true, Run, 3, Window,
        DateTime.UtcNow, "preliminary", "", cards,
        cards.Select(c => new ClusterEvaluation(c.EvaluationId, c.Subject, c.Facts, c.Norm,
            new(true, true, true, true), c.Economy, [c.Rule], c.Presentation)).ToArray(), [],
        new(cards.Length, cards.Length, cards.Length, 0), CampaignId: Campaign);

    [Fact]
    public void MoneyWinsAcrossTypesAndMeasuresExcessRatherThanWholeSpend()
    {
        var zero = Card("R03", 1500);
        var expensive = Card("R06", 10000, .4m, "reduce_bid");
        var largeButNearNorm = Card("R06", 20000, .21m, "reduce_bid", cluster: "b");
        var result = RecommendationRanking.Apply(Report(zero, largeButNearNorm, expensive));
        Assert.Equal(new[] { expensive.EvaluationId, zero.EvaluationId, largeButNearNorm.EvaluationId }, result.Cards.Select(c => c.EvaluationId));
        Assert.Equal(5000, result.Cards[0].Ranking!.WeeklyInefficientSpend);
        Assert.Equal("spend_above_norm", result.Cards[0].Ranking!.Basis);
        Assert.Equal(new[] { zero, largeButNearNorm, expensive }, Report(zero, largeButNearNorm, expensive).Cards);
    }

    [Fact]
    public void UrgentGroupAndReadyActionsPrecedeLargePreliminaryProblems()
    {
        var supply = Card("R01", 1, action: "plan_supply", scope: "article", priority: 3);
        var budget = Card("R02", 1, action: "top_up_budget", scope: "campaign", priority: 2);
        var ready = Card("R03", 100);
        var preliminary = Card("R03", 100000, preliminary: true);
        var watch = Card("R03", 50000, action: "watch");
        var result = RecommendationRanking.Apply(Report(preliminary, watch, ready, supply, budget));
        Assert.Equal(new[] { budget.EvaluationId, supply.EvaluationId, ready.EvaluationId, preliminary.EvaluationId, watch.EvaluationId }, result.Cards.Select(c => c.EvaluationId));
        Assert.Equal("review", result.Cards[3].Ranking!.Queue);
    }

    [Fact]
    public void IncompleteQualityCannotBecomeAnActionableRanking()
    {
        var card = Card("R03", 5000);
        var report = Report(card);
        report = report with { Evaluations = report.Evaluations.Select(e => e with { Quality = new(false, true, true, true, AvailableDays: 7) }).ToArray() };
        Assert.Equal("review", RecommendationRanking.Apply(report).Cards[0].Ranking!.Queue);
    }

    [Fact]
    public void MissingEconomyIsNotReplacedBySpendAndKnownZeroPrecedesUnknown()
    {
        var missing = Card("R06", 50000, action: "watch");
        var zero = Card("R06", 1000, .1m, "watch", cluster: "b");
        var result = RecommendationRanking.Apply(Report(missing, zero));
        Assert.Equal(zero.EvaluationId, result.Cards[0].EvaluationId);
        Assert.Equal(0, result.Cards[0].Ranking!.WeeklyInefficientSpend);
        Assert.Null(result.Cards[1].Ranking!.WeeklyInefficientSpend);
    }

    [Fact]
    public void SortingIsStableAndUsesWeeklyNormalization()
    {
        var b = Card("R03", 1000, cluster: "b"); var a = Card("R03", 1000);
        var ranked = RecommendationRanking.Apply(Report(b, a));
        Assert.Equal(a.EvaluationId, ranked.Cards[0].EvaluationId);
        Assert.Equal(ranked.Cards, RecommendationRanking.Apply(Report(a, b)).Cards);
        Assert.Equal(1000, ranked.Cards[0].Ranking!.WeeklyInefficientSpend);
    }

    [Fact]
    public void TopSkipsOverlappingObjectsAndKeepsScanningToFillFive()
    {
        var parent = Card("R07", 20000, .4m, "reduce_article_bid", "article");
        var children = Enumerable.Range(0, 8).Select(i => Card("R03", 1500, cluster: i.ToString())).ToArray();
        var other = Enumerable.Range(0, 4).Select(i => Card("R03", 1000, cluster: i.ToString()) with
            { Subject = children[0].Subject with { ArticleId = Guid.NewGuid(), NomenclatureId = (i + 2).ToString() } }).ToArray();
        var report = RecommendationRanking.Apply(Report([parent, .. children, .. other]));
        var top = RecommendationRanking.Top(report.Cards.Select(c => new RecommendationSummaryItem(Run, c)), Window);
        Assert.Equal(5, top.Count); Assert.Equal(parent.EvaluationId, top[0].Card.EvaluationId);
        Assert.DoesNotContain(top, x => children.Any(c => c.EvaluationId == x.Card.EvaluationId));
        Assert.Equal(13, report.Cards.Count);
    }

    [Fact]
    public void BudgetAndSupplyDoNotOverlapButStockStopDoes()
    {
        var cluster = Card("R03", 1000);
        Assert.False(RecommendationRanking.Overlaps(Card("R02", 1000, action: "top_up_budget", scope: "campaign"), cluster));
        Assert.False(RecommendationRanking.Overlaps(Card("R01", 1000, action: "plan_supply", scope: "article"), cluster));
        Assert.True(RecommendationRanking.Overlaps(Card("R01", 1000, action: "pause_campaign", scope: "article"), cluster));
        Assert.False(RecommendationRanking.Overlaps(cluster, Card("R03", 2000, cluster: "b")));
    }

    [Fact]
    public void AutoReplenishmentSuppressesOnlyBudgetAndTurningOffIgnoresItsOldReason()
    {
        var budget = Card("R02", 1000, action: "top_up_budget", scope: "campaign"); var cluster = Card("R03", 1000);
        var report = Report(budget, cluster);
        var decision = new WbSnapshotDecision { Id = Guid.NewGuid(), RunId = Run, EvaluationId = budget.EvaluationId,
            RuleId = "R02", Status = "irrelevant", Reason = "Включено автопополнение", ReasonCode = CampaignRecommendationSettings.AutoReplenishmentReason };
        var hidden = RecommendationService.ApplyDecisions(report, [decision], Window.EndDate, true);
        Assert.Equal(cluster.EvaluationId, Assert.Single(hidden.Cards).EvaluationId);
        Assert.Equal(1, hidden.Coverage.Triggered);
        var restored = RecommendationService.ApplyDecisions(report, [decision], Window.EndDate, false);
        Assert.Equal(2, restored.Cards.Count); Assert.Null(restored.Cards[0].Decision);
        decision.ReasonCode = null; // An ordinary rejection still applies.
        Assert.Single(RecommendationService.ApplyDecisions(report, [decision], Window.EndDate, false).Cards);
    }

    [Fact]
    public void MigrationModelMatchesAndAddsOnlyPreferenceAndReasonCode()
    {
        using var db = new EcomadsDbContext(new DbContextOptionsBuilder<EcomadsDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261023160908_AddPilotApplications");
        Assert.Contains("budget_auto_replenishment_enabled", sql);
        Assert.Contains("DEFAULT FALSE", sql);
        Assert.Contains("\"ReasonCode\"", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }
}

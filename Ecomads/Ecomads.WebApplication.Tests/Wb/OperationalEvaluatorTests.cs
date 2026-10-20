using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class OperationalEvaluatorTests
{
    private readonly Guid store = Guid.NewGuid(), campaign = Guid.NewGuid(), article = Guid.NewGuid();
    private readonly DateTime now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private readonly RecommendationWindow window = RecommendationPeriods.Economic(new DateOnly(2026, 10, 19));
    private WbOperationalSnapshot Snapshot(string source, object data, string entity = "") => new()
    {
        Id = Guid.NewGuid(), StoreId = store, Source = source, EntityKey = entity,
        State = "ready", RequestedAtUtc = now.AddMinutes(-10), ReceivedAtUtc = now.AddMinutes(-9),
        PayloadJson = JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };
    private IReadOnlyList<ClusterEvaluation> Evaluate(int orders = 3, int units = 1, decimal spend = 1000, decimal revenue = 1000,
        string mode = "profit", bool bid = true, bool launch = false, bool failure = false,
        IReadOnlyList<ClusterEvaluation>? clusters = null, bool incomplete = false, bool stockEnabled = true)
    {
        var metadata = new WbCampaignInfo(123, "C", 9, "cpm", [1, 2], BidType: "unified",
            ArticleBids: bid ? [new(1, 100, null)] : [], CanChangeArticles: true);
        var advertising = Snapshot("advertising", new[] { metadata });
        if (failure) { advertising.State = "access_denied"; advertising.ErrorCode = "http_403"; }
        var snapshots = new[] { advertising, Snapshot("stocks_fbw", new[] { new StockPosition(1, 1, 1, 0) }),
            Snapshot("stocks_fbs", Array.Empty<StockPosition>()), Snapshot("demand", new DemandPayload(window, [new(1, 14)])),
            Snapshot("budgets", new Dictionary<string, decimal> { ["123"] = 0 }, "123"), Snapshot("balance", new { amount = 1000 }) };
        var checks = Enumerable.Range(0, 15).Select(i => new WbCampaignDailyCheck { CampaignId = campaign, Date = window.StartDate.AddDays(i),
            Result = "data", Spend = 100 }).ToArray();
        var stats = Enumerable.Range(0, 15).Select(i => new CampaignNomenclatureStatistics { CampaignId = campaign, NomenclatureId = article,
            Date = window.StartDate.AddDays(i).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Spend = i == 0 ? spend : 1, Revenue = i == 0 ? revenue : 1, Orders = i == 0 ? orders : 0,
            OrderedProducts = i == 0 ? units : 0, RecommendationFieldsVerified = true }).ToArray();
        var settings = new WbAdvertisingSettings { CampaignId = campaign, PayloadJson = JsonSerializer.Serialize(new AdvertisingSettingsPayload([], [], metadata, now.AddMinutes(-20)), new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        return OperationalEvaluator.Evaluate(store, campaign, "123", 9,
            [new() { Id = article, StoreId = store, WbNomenclatureId = "1", Name = "Product" }], stats,
            [], incomplete ? checks.Skip(1).ToArray() : checks, [], snapshots, settings, null,
            new() { CampaignId = campaign, Version = 7, RecommendationMode = mode, LaunchDate = launch ? window.StartDate : null }, clusters ?? [], window, now, stockEnabled);
    }

    [Fact]
    public void CampaignRulePreservesTheSettingsVersionInItsEvidence()
    {
        var budget = Evaluate(stockEnabled: false).Single(x => x.Rules[0].RuleId == "R02");
        Assert.Equal(7, budget.Norm.CampaignVersion);
    }

    [Fact]
    public void DisabledStockRuleHasNoCardsOrMissingSourceWarningsAndKeepsArticleRule()
    {
        Assert.False(new RecommendationOptions().Rule01Enabled);
        var results = Evaluate(stockEnabled: false);
        Assert.DoesNotContain(results, x => x.Rules.Any(r => r.RuleId == "R01"));
        Assert.Equal("triggered", results.Single(x => x.Rules[0].RuleId == "R07").Rules[0].Status);
    }

    [Fact]
    public void ArticleRuleUsesOrdersNotUnitsAndConfirmedPointScope()
    {
        var r = Evaluate().Single(x => x.Rules[0].RuleId == "R07");
        Assert.Equal("triggered", r.Rules[0].Status);
        Assert.Equal("reduce_article_bid", r.Presentation!.Primary.Kind);
        Assert.Equal(30m, r.Presentation.Primary.ReductionPercent);
        Assert.Null(r.Presentation.Primary.TargetBid);
        Assert.Equal("not_triggered", Evaluate(orders: 2, units: 100).Single(x => x.Rules[0].RuleId == "R07").Rules[0].Status);
    }

    [Fact]
    public void ModesAndStrictThresholdAreRespected()
    {
        // Total spend 253 and revenue 1013 give 24.975%: above profit 24%, below growth 30%.
        Assert.Equal("triggered", Evaluate(spend: 240, revenue: 1000).Single(x => x.Rules[0].RuleId == "R07").Rules[0].Status);
        Assert.Equal("not_triggered", Evaluate(spend: 240, revenue: 1000, mode: "growth").Single(x => x.Rules[0].RuleId == "R07").Rules[0].Status);
        Assert.Equal("skipped", Evaluate(launch: true).Single(x => x.Rules[0].RuleId == "R07").Rules[0].Status);
        var partial = Evaluate(incomplete: true).Single(x => x.Rules[0].RuleId == "R07");
        Assert.Equal("not_triggered", partial.Rules[0].Status); // The unavailable day held all three orders.
        Assert.Equal(13, partial.Quality.AvailableDays);
        Assert.False(partial.Quality.Complete);
        Assert.Equal(13m, partial.Facts.Spend);
    }

    [Fact]
    public void UnknownScopeNeverLowersOtherProductsAndLatestFailureInvalidatesCache()
    {
        var r = Evaluate(bid: false).Single(x => x.Rules[0].RuleId == "R07");
        Assert.Equal("check_product", r.Presentation!.Primary.Kind);
        Assert.Contains(r.Presentation.Restrictions, x => x.Code == "scope_unknown");
        var failed = Evaluate(failure: true);
        Assert.Equal("skipped", failed.Single(x => x.Rules[0].RuleId == "R07").Rules[0].Status);
        Assert.Contains("WB запретил доступ", failed.Single(x => x.Rules[0].RuleId == "R01").Rules[0].Explanation);
    }

    [Fact]
    public void PendingExclusionsBlockReductionEvenAfterAcceptedDecision()
    {
        var f = new RecommendationFacts(1000, 1000, 30, 10, 0, 0, 1000);
        var n = new RecommendationNorm(.2m, "default", 0, 0, 0);
        var p = new RecommendationPresentation(new("exclude", "Исключите кластер"), [], 0, false, 500, [], [], null,
            new("ready", "unified", "cpm", false));
        var cluster = new ClusterEvaluation(Guid.NewGuid(), new(store, campaign, article, "1", "Product", "bad"), f, n,
            new(true, true, true, true), RecommendationEngine.Economy(f, n, .2m), [new("R03", 1, 2, true, "triggered", "test", "test", 300)], p);
        var pending = Evaluate(clusters: [cluster]).Single(x => x.Rules[0].RuleId == "R07");
        Assert.Equal("check_product", pending.Presentation!.Primary.Kind);
        Assert.Contains(pending.Presentation.Restrictions, x => x.Code == "exclude_first");
        var applied = cluster with { Presentation = p with { Control = p.Control! with { Excluded = true } } };
        Assert.Equal("reduce_article_bid", Evaluate(clusters: [applied]).Single(x => x.Rules[0].RuleId == "R07").Presentation!.Primary.Kind);
    }

    [Fact]
    public void StockWarningSurvivesLaunchAndDoesNotPauseMultiProductCampaign()
    {
        var stock = Evaluate(launch: true).Single(x => x.Rules[0].RuleId == "R01");
        Assert.Equal("triggered", stock.Rules[0].Status);
        Assert.Equal("remove_article", stock.Presentation!.Primary.Kind);
        Assert.True(stock.Presentation.Details!.BlockGrowth);
    }
}

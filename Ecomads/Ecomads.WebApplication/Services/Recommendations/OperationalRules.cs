namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record SourceEvidence(Guid? SnapshotId, string Source, string State, DateTime? RequestedAtUtc,
    DateTime? ReceivedAtUtc, string? ErrorCode = null)
{
    public bool Fresh(DateTime now) => State == "ready" && RequestedAtUtc <= now && ReceivedAtUtc <= now &&
        RequestedAtUtc >= now.AddHours(-1);
}
public sealed record OperationalDetails(string Kind, RecommendationWindow Window, IReadOnlyList<SourceEvidence> Sources,
    decimal? Fbw = null, decimal? Fbs = null, decimal? StockDays = null, decimal? DailySpend = null,
    decimal? DailyUnits = null, decimal? Budget = null, decimal? BudgetDays = null, decimal? TopUp = null,
    decimal? Balance = null, IReadOnlyList<string>? AffectedArticles = null, bool BlockGrowth = false);
public sealed record StockSignal(string Status, string Reason, int Priority, RecommendationAction Action,
    decimal? StockDays, bool BlockGrowth, string? Warning = null);
public sealed record BudgetSignal(string Status, string Reason, decimal? Days, decimal? TopUp, bool InsufficientBalance);

public static class OperationalRules
{
    public static StockSignal Stock(decimal? fbw, decimal? fbs, int? units, decimal spend3,
        bool spendComplete, bool sourcesFresh, string mode, bool singleProduct, bool removable,
        bool manualBid)
    {
        var watch = new RecommendationAction("watch", "Дождитесь подтверждённых данных", Level: "article");
        if (!sourcesFresh || fbw is null or < 0 || fbs is null or < 0 || !spendComplete)
            return new("skipped", "stocks_incomplete", 4, watch, null, true);
        var stock = fbw.Value + fbs.Value;
        var days = units > 0 ? stock * 14 / units.Value : (decimal?)null;
        if (spend3 <= 0) return new("not_triggered", "no_recent_spend", 4, watch, days, days < 7 || stock == 0);
        if (stock == 0)
            return new("triggered", "out_of_stock", 0, singleProduct
                ? new("pause_campaign", "Приостановите кампанию до появления остатка", Level: "campaign")
                : removable ? new("remove_article", "Уберите товар из рекламы до появления остатка", Level: "article")
                : new("check_product", "Проверьте возможность остановить рекламу этого товара", Level: "article"), 0, true,
                !singleProduct && !removable ? "Пауза всей кампании затронет другие товары; точечное управление не подтверждено." : null);
        if (!units.HasValue || units < 0) return new("skipped", "demand_incomplete", 4, watch, null, true);
        if (units == 0) return new("not_triggered", "zero_demand", 4, watch, null, false);
        if (days < 3)
            return new("triggered", "stock_below_3_days", 1, manualBid
                ? new("reduce_article_bid", "Снизьте ставку товара на 30%", 30, Level: "article")
                : removable ? new("remove_article", "Временно уберите товар из рекламы", Level: "article")
                : new("check_product", "Проверьте рекламу товара: запас меньше трёх дней", Level: "article"), days, true);
        if (days < 7) return new("triggered", "stock_below_7_days", 3,
            new(mode == "clearance" ? "watch" : "plan_supply", mode == "clearance" ? "Учтите остаток при завершении распродажи" : "Запланируйте поставку и не усиливайте рекламу", Level: "article"), days, true);
        return new("not_triggered", "stock_sufficient", 4, watch, days, false);
    }

    public static BudgetSignal Budget(decimal spend7, decimal revenue7, decimal norm, decimal? budget,
        decimal? balance, bool complete, bool fresh, bool running)
    {
        if (!complete || !fresh || budget is null or < 0 || balance < 0 || spend7 < 0 || revenue7 < 0)
            return new("skipped", "budget_incomplete", null, null, false);
        if (!running || spend7 <= 0 || revenue7 <= 0 || spend7 / revenue7 > norm)
            return new("not_triggered", "campaign_not_eligible", null, null, false);
        var daily = spend7 / 7;
        var days = budget / daily;
        var topUp = Math.Max(0, 3 * daily - budget.Value);
        return new(days < 1 ? "triggered" : "not_triggered", days < 1 ? "budget_below_day" : "budget_sufficient",
            days, days < 1 ? decimal.Ceiling(topUp * 100) / 100 : null, balance.HasValue && balance < topUp);
    }

    public static RecommendationAction Reduce(decimal percent, decimal? bid, decimal? minimum, decimal? step, string level)
    {
        percent = Math.Clamp(percent, 0, 30);
        if (percent <= 0) return new("watch", "Снижение ставки не требуется", Level: level);
        decimal? target = null;
        if (bid > 0 && minimum > 0 && step > 0)
        {
            var candidate = decimal.Ceiling(Math.Max(minimum.Value, bid.Value * (1 - percent / 100)) / step.Value) * step;
            if (candidate >= bid) return new("watch", "Ставка уже на минимуме; снижение недоступно", Level: level);
            target = candidate;
        }
        return new("reduce_" + level + "_bid", target.HasValue ? $"Снизьте ставку до {target:0.##} ₽" : $"Снизьте ставку на {percent:0.##}%", percent, target, level);
    }
}

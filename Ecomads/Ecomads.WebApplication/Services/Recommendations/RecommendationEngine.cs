namespace Ecomads.WebApplication.Services.Recommendations;

public sealed class RecommendationOptions
{
    public bool Enabled { get; set; } = true;
    // Deferred from sprint 3 until both FBW and FBS are available with supported access.
    public bool Rule01Enabled { get; set; } = false;
    public bool SourcesComparable { get; set; } = true;
    public bool Rule03Enabled { get; set; } = true;
    public bool Rule06Enabled { get; set; } = true;
    public decimal PriceScenario { get; set; } = 0.20m;
    public decimal NoUnitsSpendFloor { get; set; } = 300m;
    public int HighDrrMinimumUnits { get; set; } = 3;
    public int RetentionDays { get; set; } = 90;

}

public sealed record RecommendationWindow(DateOnly StartDate, DateOnly EndDate)
{
    public int Days => EndDate.DayNumber - StartDate.DayNumber + 1;
    public bool IsValid => Days is >= 1 and <= 366 && StartDate.DayNumber >= Days;
}

public sealed record RecommendationNorm(decimal Value, string Source, int StoreVersion,
    int CampaignVersion, int ArticleVersion);

public sealed record RecommendationQuality(bool FullStatsComplete, bool ClustersComplete,
    bool FieldsVerified, bool SourcesComparable, string Maturity = "unverified")
{
    public bool Complete => FullStatsComplete && ClustersComplete;
}

public sealed record RecommendationFacts(decimal Spend, int? Views, int? Clicks, int? Carts,
    int? Orders, int? OrderedProducts, decimal? UnitPrice);

public sealed record RecommendationEconomy(decimal? EstimatedOrderSum, decimal? Drr,
    decimal? DrrMin, decimal? DrrMax, decimal? DrrPlusOne, decimal? AllowedSpendPerUnit);

public sealed record RuleEvaluation(string RuleId, int RuleVersion, int Group, bool Enabled,
    string Status, string ReasonCode, string Explanation, decimal? Threshold, string ActionKind = "review",
    string Tier = "free");

/// <summary>Pure rules. All rates are decimal fractions; rendering is the only rounding boundary.</summary>
public static class RecommendationEngine
{
    public const int Version = 1;

    public static RecommendationNorm ResolveNorm(decimal? articlePercent, decimal? campaignPercent,
        decimal? storePercent, int articleVersion = 0, int campaignVersion = 0, int storeVersion = 0) =>
        new((articlePercent ?? campaignPercent ?? storePercent ?? 20m) / 100m,
            articlePercent.HasValue ? "article" : campaignPercent.HasValue ? "campaign" :
            storePercent.HasValue ? "store" : "default", storeVersion, campaignVersion, articleVersion);

    public static RecommendationEconomy Economy(RecommendationFacts facts, RecommendationNorm norm, decimal delta)
    {
        if (delta is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(delta));
        var sum = facts.UnitPrice is >= 0 && facts.OrderedProducts is >= 0
            ? facts.UnitPrice * facts.OrderedProducts : null;
        var drr = sum > 0 ? facts.Spend / sum : null;
        var plus = facts.UnitPrice > 0 && facts.OrderedProducts is >= 0
            ? facts.Spend / ((facts.OrderedProducts.Value + 1m) * facts.UnitPrice) : null;
        return new(sum, drr, drr / (1 + delta), drr / (1 - delta), plus,
            facts.UnitPrice > 0 ? facts.UnitPrice * norm.Value : null);
    }

    public static IReadOnlyList<RuleEvaluation> Evaluate(RecommendationFacts facts, RecommendationNorm norm,
        RecommendationQuality quality, RecommendationOptions options)
    {
        var economy = Economy(facts, norm, options.PriceScenario);
        string? skip = !quality.Complete ? "incomplete_data" : !quality.SourcesComparable ? "sources_unverified" :
            !quality.FieldsVerified ? "fields_unverified" : facts.OrderedProducts is null ? "units_missing" :
            facts.Spend < 0 || facts.OrderedProducts < 0 || norm.Value < 0 ? "invalid_values" :
            facts.UnitPrice is null or <= 0 ? "unit_price_unavailable" : null;
        RuleEvaluation Result(string id, int group, bool enabled, string status, string code,
            string explanation, decimal? threshold) => new(id, Version, group, enabled,
                enabled ? status : "disabled", enabled ? code : "rule_disabled", explanation, threshold);
        var threshold = economy.AllowedSpendPerUnit.HasValue
            ? Math.Max(2m * economy.AllowedSpendPerUnit.Value, options.NoUnitsSpendFloor) : (decimal?)null;
        if (skip != null)
            return [Result("R03", 2, options.Rule03Enabled, "skipped", skip, ExplainSkip(skip), threshold),
                Result("R06", 3, options.Rule06Enabled, "skipped", skip, ExplainSkip(skip), norm.Value)];
        var noUnits = facts.OrderedProducts == 0 && facts.Spend >= threshold;
        var high = facts.OrderedProducts >= options.HighDrrMinimumUnits && economy.DrrMin > norm.Value &&
            economy.DrrPlusOne > norm.Value;
        return [Result("R03", 2, options.Rule03Enabled, noUnits ? "triggered" : "not_triggered",
                noUnits ? "spend_without_units" : "condition_not_met",
                noUnits ? "Проверьте расходы и соответствие запросов товару: денежный порог достигнут, заказанных единиц нет."
                    : "Условие расхода без заказанных единиц не выполнено.", threshold),
            Result("R06", 3, options.Rule06Enabled, high ? "triggered" : "not_triggered",
                high ? "robust_high_drr" : facts.OrderedProducts < options.HighDrrMinimumUnits ? "low_sample" : "condition_not_met",
                high ? "Проверьте расходы, настройки рекламы и карточку: превышение нормы сохраняется в сценарии цены и при добавлении одной единицы."
                    : facts.OrderedProducts < options.HighDrrMinimumUnits ? "Для проверки высокого ДРР нужно больше заказанных единиц."
                    : "Устойчивое превышение нормы не обнаружено.", norm.Value)];
    }

    public static string ExplainSkip(string code) => code switch
    {
        "incomplete_data" => "Не все дни товарной статистики или кластеров за выбранный период загружены. Ожидается загрузка WB.",
        "sources_unverified" => "Экономика недоступна: сопоставимость источников ещё не подтверждена.",
        "fields_unverified" => "Экономика недоступна: поля старой статистики требуют повторной загрузки.",
        "units_missing" => "Неизвестно количество заказанных единиц; оно не заменяется числом заказов.",
        "invalid_values" => "Исходные значения некорректны.",
        _ => "Экономика недоступна: средняя сумма на единицу за выбранный период не определена."
    };
}

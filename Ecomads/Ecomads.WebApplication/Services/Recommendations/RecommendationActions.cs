namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record AdvertisingControl(string Status, string? BidType = null, string? PaymentType = null,
    bool? Excluded = null, decimal? CurrentBid = null, decimal? MinimumBid = null, decimal? BidStep = null, DateTime? ReceivedAtUtc = null);
public sealed record RecommendationAction(string Kind, string Label, decimal? ReductionPercent = null,
    decimal? TargetBid = null, string Level = "cluster");
public sealed record RecommendationRestriction(string Code, string Label);
public sealed record RecommendationPresentation(RecommendationAction Primary, IReadOnlyList<RecommendationAction> Alternatives,
    decimal? KeyShare, bool IsKey, decimal WeeklySpend, IReadOnlyList<RecommendationRestriction> Restrictions,
    IReadOnlyList<RecommendationDiagnostic> Diagnostics, string? Warning = null, AdvertisingControl? Control = null,
    OperationalDetails? Details = null, int Priority = 4, RecommendationClickEvidence? ClickEvidence = null);
public sealed record RecommendationDiagnostic(string Code, string Label, string Basis);
public sealed record RecommendationDecisionState(string Status, DateOnly? SnoozeUntil, string? Reason, string? Comment,
    DateTime CreatedAtUtc);
public sealed record RecommendationPackage(string Id, Guid CampaignId, IReadOnlyList<Guid> EvaluationIds, decimal WeeklySpend);

public static class RecommendationActions
{
    public static bool LaunchProtected(DateOnly? launch, RecommendationWindow window, DateOnly today) => launch.HasValue &&
        (today >= launch && today < launch.Value.AddDays(21) ||
         window.StartDate < launch.Value.AddDays(21) && window.EndDate >= launch);

    public static RecommendationPresentation Select(RuleEvaluation rule, RecommendationFacts facts,
        RecommendationNorm norm, RecommendationQuality quality, RecommendationEconomy economy,
        AdvertisingControl control, int? articleUnits, DateOnly? launch, RecommendationWindow window, DateOnly today,
        RecommendationClickEvidence? clickEvidence = null)
    {
        decimal? share = articleUnits > 0 && facts.OrderedProducts is >= 0 && facts.OrderedProducts <= articleUnits
            ? (decimal)facts.OrderedProducts.Value / articleUnits : articleUnits == 0 && facts.OrderedProducts == 0 ? 0 : null;
        var key = share >= .2m;
        var restrictions = new List<RecommendationRestriction>();
        if (LaunchProtected(launch, window, today)) restrictions.Add(new("launch", "Защита запуска: первые 21 день. Расходные рекомендации скрыты."));
        if (!quality.Complete) restrictions.Add(new("incomplete", $"Предварительно: расчёт по {quality.AvailableDays ?? 0} из {quality.ExpectedDays ?? window.Days} дней."));
        if (window.EndDate > today.AddDays(-2)) restrictions.Add(new("recent", "Свежий период: только предварительная диагностика. Для действий выберите конец не позднее D−2 по МСК."));
        if (control.Status != "ready") restrictions.Add(new("settings_" + control.Status, control.Status == "stale"
            ? "Настройки рекламы устарели; дождитесь обновления." : control.Status == "unsupported"
            ? "Кампания CPC: WB не поддерживает управление ставками и исключениями поисковых кластеров для этого типа оплаты."
            : "Настройки рекламы неизвестны или недоступны; действие не подтверждено."));
        if (share is null && !(rule.RuleId == "R03" && facts.OrderedProducts == 0)) restrictions.Add(new("key_unknown", "Ключевая доля неизвестна: исключение недоступно."));
        if (key) restrictions.Add(new("key", "Ключевой кластер: не менее 20% заказанных единиц товара. Исключение запрещено."));
        if (control.Excluded == true) restrictions.Add(new("excluded", "Кластер уже исключён."));
        var watch = new RecommendationAction("watch", "Наблюдать");
        var primary = watch;
        var alternatives = new List<RecommendationAction>();
        bool actionable = quality.Complete && quality.FieldsVerified && quality.SourcesComparable &&
            window.EndDate <= today.AddDays(-2) && !LaunchProtected(launch, window, today);
        var manual = control.BidType == "manual" && control.PaymentType == "cpm";
        if (manual && (control.CurrentBid is null or <= 0 || control.MinimumBid is null or <= 0 || control.BidStep is null or <= 0))
            restrictions.Add(new("bid_constraints_unknown", "Точная сумма ставки недоступна: текущая ставка, минимум или шаг WB не подтверждены. Снижение указано в процентах."));
        if (rule.RuleId == "R06" && control.BidType == "unified" && !key && economy.Drr < 2 * norm.Value)
            restrictions.Add(new("unified_below_exclusion", "Единая ставка: для исключения неключевого кластера нужен ДРР не ниже 2 × нормы. Пока наблюдать."));
        if (control.Status == "ready" && !manual && control.BidType != "unified")
            restrictions.Add(new("unsupported_control", "Доступный способ управления ставкой этого кластера не подтверждён."));
        RecommendationAction Reduce(decimal pct)
        {
            decimal? target = null;
            if (control.CurrentBid > 0 && control.MinimumBid > 0 && control.BidStep > 0)
            {
                // Round upwards so neither the minimum nor the 30% bound can be violated.
                var raw = Math.Max(control.MinimumBid.Value, control.CurrentBid.Value * (1 - pct / 100));
                var candidate = decimal.Ceiling(raw / control.BidStep.Value) * control.BidStep.Value;
                if (candidate < control.CurrentBid) target = candidate;
                else return new("watch", "Ставка уже на минимуме; наблюдать");
            }
            return new("reduce_bid", target.HasValue ? $"Снизьте ставку до {target:0.##} ₽" : $"Снизьте ставку на {pct:0.##}%", pct, target);
        }
        if (rule.RuleId == "R03")
        {
            clickEvidence ??= RecommendationClickEvidence.Calculate(
                new(window.EndDate.AddDays(-29), window.EndDate), 0, 0, false, facts.Clicks);
            var eligible = actionable && rule.Status == "triggered" && facts.OrderedProducts == 0 &&
                rule.Threshold.HasValue && facts.Spend >= rule.Threshold &&
                control.Status == "ready" && control.Excluded == false && (manual || control.BidType == "unified");
            if (eligible && clickEvidence.Result == "sufficient")
            {
                primary = new("exclude", "Исключите кластер");
                if (manual) alternatives.Add(Reduce(30));
            }
            else if (eligible && clickEvidence.Result == "insufficient")
            {
                restrictions.Add(new("insufficient_clicks", "Мало кликов для вывода, возможно, дело в цене клика"));
                if (manual) primary = Reduce(30);
                else primary = new("watch", "Наблюдать: мало кликов для вывода, возможно, дело в цене клика");
            }
            else if (clickEvidence.Result == "unknown")
                restrictions.Add(new("clicks_unknown", "Число кликов неизвестно; дождитесь загрузки данных."));
        }
        else if (actionable && key && control.Status != "ready")
            primary = new("check_product", "Проверьте цену и карточку", Level: "article");
        else if (actionable && control.Status == "ready" && control.Excluded != true)
        {
            if (manual)
                primary = Reduce(economy.Drr > 0 ? Math.Clamp((1 - norm.Value / economy.Drr.Value) * 100, 0, 30) : 30);
            else if (key) primary = new("check_product", "Проверьте цену и карточку", Level: "article");
            else if (control.BidType == "unified" && share.HasValue && control.Excluded == false && economy.Drr >= 2 * norm.Value)
                primary = new("exclude", "Исключите кластер");
        }
        return new(primary, alternatives, share, key, facts.Spend / Math.Max(1, quality.AvailableDays ?? window.Days) * 7, restrictions, [],
            rule.RuleId == "R03" ? "Проверьте, что запрос подходит товару." : null, control,
            ClickEvidence: rule.RuleId == "R03" ? clickEvidence : null);
    }

    public static IReadOnlyList<RecommendationPackage> Packages(IEnumerable<RecommendationCard> cards) => cards
        .Where(x => x.Rule.RuleId == "R03" && x.Presentation?.Primary.Kind == "exclude")
        .GroupBy(x => x.Subject.CampaignId).Select(g => g.DistinctBy(x => (x.Subject.ArticleId, x.Subject.ClusterName)).ToArray())
        .Where(g => g.Length >= 5).Select(g => new RecommendationPackage(g[0].Subject.CampaignId + ":R03",
            g[0].Subject.CampaignId, g.Select(x => x.EvaluationId).ToArray(), g.Sum(x => x.Presentation!.WeeklySpend))).ToArray();
}

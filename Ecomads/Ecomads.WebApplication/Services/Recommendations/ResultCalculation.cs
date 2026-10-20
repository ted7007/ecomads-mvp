namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record ResultObjectMetrics(string Key, string Article, decimal Spend, int Clicks, int Units,
    decimal? Revenue, int? TotalUnits, decimal? UnitPrice, int KnownStockDays, int ZeroStockDays, int PausedZeroDays);
public sealed record ResultMetrics(decimal CampaignSpend, decimal CampaignRevenue, decimal OtherCampaignSpend,
    IReadOnlyList<ResultObjectMetrics> Objects, IReadOnlyList<string> Gaps, IReadOnlyList<string> Sources)
{
    public decimal? CampaignDrr => CampaignRevenue > 0 ? CampaignSpend / CampaignRevenue : null;
}
public sealed record ResultOutcome(string Status, string Reason, decimal? Savings, string Formula);
public sealed record ResultMember(Guid RecommendationId, Guid ExposureId, RecommendationCard Card,
    ResultSetting Before, ResultSetting Applied);
public sealed record ResultEvidence(int Version, ResultMetrics Before, ResultMetrics After, ResultOutcome Outcome,
    IReadOnlyList<string> Restrictions);

public static class ResultCalculation
{
    public const int Version = 1;
    public static bool FellMore(decimal before, decimal after, decimal fraction) => before > 0 && after < before * (1 - fraction);
    public static bool ChangedMore(decimal before, decimal after, decimal fraction) =>
        before == 0 ? after != 0 : Math.Abs(after - before) > before * fraction;

    public static ResultOutcome Calculate(IReadOnlyList<ResultMember> members, ResultMetrics before, ResultMetrics after,
        IReadOnlyList<string> restrictions)
    {
        ResultOutcome Unavailable(string reason) => new("not_evaluated", reason, null, "Денежный результат не рассчитан");
        if (members.Count == 0) return Unavailable("Нет доказательств применения");
        if (members.Select(x => (x.Card.Rule.RuleId, x.Card.Presentation!.Primary.Kind)).Distinct().Count() > 1)
            return Unavailable("Методика смешанного пакета не согласована");
        var card = members[0].Card; var rule = card.Rule.RuleId; var action = card.Presentation!.Primary.Kind;
        if (rule == "R02") return new("done", "Пополнение обнаружено по снимкам бюджета", null, "Без оценки в рублях");
        if (!(rule == "R03" && action == "exclude" || rule is "R06" or "R07" && action.StartsWith("reduce_") ||
            rule == "R01" && action is "pause_campaign" or "remove_article"))
            return Unavailable("Методика для этого действия не согласована");
        var gaps = before.Gaps.Concat(after.Gaps).Concat(restrictions).Distinct().ToList();
        if (ChangedMore(before.OtherCampaignSpend, after.OtherCampaignSpend, .2m))
            gaps.Add("Расход остальных кампаний изменился больше чем на 20%");
        foreach (var b in before.Objects)
        {
            var a = after.Objects.SingleOrDefault(x => x.Key == b.Key);
            if (a == null) { gaps.Add("Нет сопоставимых данных объекта"); continue; }
            if (b.KnownStockDays != 7 || a.KnownStockDays != 7) gaps.Add("Неполная история остатков");
            if (rule != "R01" && (a.ZeroStockDays > 0 || b.ZeroStockDays > 0)) gaps.Add("Товар отсутствовал в наличии");
            if (b.TotalUnits == null || a.TotalUnits == null) gaps.Add("Нет всех заказанных единиц товара");
            if (rule != "R01" && (b.UnitPrice is null or <= 0 || a.UnitPrice is null or <= 0)) gaps.Add("Нет сопоставимой средней цены товара");
            else if (b.UnitPrice > 0 && a.UnitPrice.HasValue && ChangedMore(b.UnitPrice.Value, a.UnitPrice.Value, .1m))
                gaps.Add("Средняя цена изменилась больше чем на 10%");
        }
        if (gaps.Count > 0) return Unavailable(string.Join("; ", gaps.Distinct()));
        if (rule == "R01")
        {
            var avoided = before.Objects.Sum(b => b.Spend / 7m * after.Objects.Single(a => a.Key == b.Key).PausedZeroDays);
            return new(avoided > 0 ? "confirmed" : "no_effect", "Избежанный расход за подтверждённые дни остановки при нулевом остатке",
                decimal.Round(avoided, 2), "Σ(расход объекта до / 7 × дни остановки при нулевом остатке)");
        }
        foreach (var b in before.Objects)
        {
            var a = after.Objects.Single(x => x.Key == b.Key);
            if (FellMore(b.TotalUnits!.Value, a.TotalUnits!.Value, .15m))
                return new("not_confirmed", "Все заказанные единицы товара упали больше чем на 15%", null, "Защита заказов товара");
            if (rule is "R06" or "R07")
            {
                if (b.Revenue is null or <= 0 || a.Revenue is null or <= 0) return Unavailable("ДРР объекта не определён");
                if (FellMore(b.Units, a.Units, .3m))
                    return new("not_confirmed", "Рекламные единицы объекта упали больше чем на 30%", null, "Защита рекламных заказов");
                if (a.Spend / a.Revenue >= b.Spend / b.Revenue)
                    return new("no_effect", "ДРР объекта не снизился", 0, "Сравнение ДРР объекта до и после");
            }
        }
        var cap = rule == "R07" ? before.CampaignSpend : before.Objects.Sum(x => x.Spend);
        var savings = decimal.Round(Math.Max(0, Math.Min(before.CampaignSpend - after.CampaignSpend, cap)), 2);
        if (savings == 0 && before.CampaignDrr.HasValue && after.CampaignDrr < before.CampaignDrr)
            return new("redistributed", $"Перераспределено: ДРР {before.CampaignDrr:P1} → {after.CampaignDrr:P1}", null, "Изменение ДРР кампании без снижения расхода");
        return new(savings > 0 ? "confirmed" : "no_effect", savings > 0 ? "Критерии семидневного замера выполнены" : "Расход кампании не снизился", savings,
            rule == "R07" ? "max(0; min(расход кампании до − после; расход кампании до))" :
                "max(0; min(расход кампании до − после; расход уникальных объектов до))");
    }
}

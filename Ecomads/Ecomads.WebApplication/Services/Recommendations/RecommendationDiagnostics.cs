namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record DiagnosticThresholds(int Clicks = 30, int Views = 1000, int Carts = 20,
    int Orders = 3, decimal Spend = 500, decimal? Ctr = null, decimal? CartConversion = null,
    decimal? OrderCartConversion = null, decimal? OrderClickConversion = null);

public static class RecommendationDiagnostics
{
    public static IReadOnlyList<RecommendationDiagnostic> Calculate(RecommendationFacts current,
        RecommendationEconomy economy, RecommendationNorm norm, IReadOnlyList<RecommendationFacts> peers,
        RecommendationFacts? previous, RecommendationEconomy? previousEconomy, DiagnosticThresholds thresholds)
    {
        var found = new List<(int Priority, RecommendationDiagnostic Signal)>();
        void Add(int priority, string code, string label, string basis) => found.Add((priority, new(code, label, basis)));
        decimal? Ratio(int? n, int? d) => n >= 0 && d > 0 ? (decimal)n.Value / d.Value : null;
        decimal? Peer(Func<RecommendationFacts, int?> numerator, Func<RecommendationFacts, int?> denominator, int min)
        {
            var adequate = peers.Where(x => denominator(x) >= min && numerator(x) >= 0).ToArray();
            return adequate.Length >= 3 && adequate.Sum(x => denominator(x)) > 0
                ? (decimal)adequate.Sum(x => numerator(x))! / adequate.Sum(x => denominator(x))! : null;
        }
        var ctr = Ratio(current.Clicks, current.Views);
        var cart = Ratio(current.Carts, current.Clicks);
        var orderCart = Ratio(current.Orders, current.Carts);
        var cr = Ratio(current.Orders, current.Clicks);
        bool Below(decimal? value, decimal? explicitNorm, decimal? peer) => value.HasValue &&
            (explicitNorm.HasValue ? value < explicitNorm : peer > 0 && value < .7m * peer);
        var f02 = current.Clicks >= thresholds.Clicks && Below(cart, thresholds.CartConversion, Peer(x => x.Carts, x => x.Clicks, thresholds.Clicks));
        var f03 = current.Carts >= thresholds.Carts && Below(orderCart, thresholds.OrderCartConversion, Peer(x => x.Orders, x => x.Carts, thresholds.Carts));
        var f04 = current.Clicks >= thresholds.Clicks && Below(cr, thresholds.OrderClickConversion, Peer(x => x.Orders, x => x.Clicks, thresholds.Clicks));
        const string peerBasis = "Гипотеза; заданная норма или совокупная конверсия минимум трёх других кластеров того же товара и кампании.";
        if (current.Views >= thresholds.Views && Below(ctr, thresholds.Ctr, Peer(x => x.Clicks, x => x.Views, thresholds.Views)))
            Add(3, "F01", "Гипотеза: низкая кликабельность; проверьте соответствие запроса и главное фото.", peerBasis);
        if (f02) Add(3, "F02", "Гипотеза: мало добавлений в корзину; проверьте цену и карточку товара.", peerBasis);
        if (f03) Add(3, "F03", "Гипотеза: корзины редко становятся заказами; проверьте условия покупки.", peerBasis);
        if (f04 && !f02 && !f03) Add(3, "F04", "Гипотеза: низкая конверсия кликов в заказы.", peerBasis);
        var adequateCpc = peers.Where(x => x.Clicks >= thresholds.Clicks && x.Spend >= 0).ToArray();
        var peerCpc = adequateCpc.Length >= 3 && adequateCpc.Sum(x => x.Clicks) > 0
            ? adequateCpc.Sum(x => x.Spend) / adequateCpc.Sum(x => x.Clicks) : null;
        var cpc = current.Clicks > 0 ? current.Spend / current.Clicks : null;
        if (current.Clicks >= thresholds.Clicks && peerCpc > 0 && cpc > 1.3m * peerCpc && (economy.DrrMin > norm.Value || f04))
            Add(4, "F05", "Гипотеза: дорогие клики ухудшают экономику.", "Стоимость клика выше 130% совокупной стоимости клика минимум трёх других кластеров.");
        if (previous is not null && previousEconomy is not null)
        {
            var priorCr = Ratio(previous.Orders, previous.Clicks);
            var priorCpc = previous.Clicks > 0 ? previous.Spend / previous.Clicks : null;
            var bad = economy.Drr > norm.Value || current.Orders == 0;
            var sufficient = current.Orders >= thresholds.Orders && previous.Orders >= thresholds.Orders &&
                current.Spend >= thresholds.Spend && previous.Spend >= thresholds.Spend;
            const string prior = "Гипотеза; сравнение с непосредственно предыдущим полным периодом той же длины. Цена рассчитана отдельно для каждого периода.";
            if (sufficient && previousEconomy.Drr > 0 && economy.Drr >= 1.3m * previousEconomy.Drr && economy.DrrMin > norm.Value)
                Add(1, "D01", "Гипотеза: экономика ухудшилась; оценочный ДРР вырос минимум на 30%.", prior);
            if (current.Spend >= thresholds.Spend && previous.Spend >= thresholds.Spend && previous.Spend > 0 &&
                current.Spend >= 1.4m * previous.Spend && previous.Orders > 0 && current.Orders <= 1.1m * previous.Orders && bad)
                Add(2, "D02", "Гипотеза: расход растёт быстрее заказов.", prior);
            if (sufficient && economy.Drr <= norm.Value && previousEconomy.Drr <= norm.Value &&
                (previous.Clicks > 0 && current.Clicks <= .6m * previous.Clicks || previous.Views > 0 && current.Views <= .6m * previous.Views))
                Add(2, "D03", "Гипотеза: объём эффективного трафика снизился; проверьте причины.", prior);
            if (current.Clicks >= thresholds.Clicks && previous.Clicks >= thresholds.Clicks)
            {
                if (priorCpc > 0 && cpc >= 1.3m * priorCpc && cr.HasValue && priorCr.HasValue && cr <= priorCr && bad)
                    Add(4, "D04", "Гипотеза: стоимость клика выросла без роста конверсии.", prior);
                if (priorCr > 0 && cr <= .7m * priorCr) Add(3, "D05", "Гипотеза: конверсия кликов в заказы снизилась минимум на 30%.", prior);
            }
        }
        return found.OrderBy(x => x.Priority).ThenBy(x => x.Signal.Code, StringComparer.Ordinal).Take(2).Select(x => x.Signal).ToArray();
    }
}

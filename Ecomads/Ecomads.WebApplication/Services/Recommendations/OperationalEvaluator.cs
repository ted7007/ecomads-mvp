using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;

namespace Ecomads.WebApplication.Services.Recommendations;

public static class OperationalEvaluator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static IReadOnlyList<ClusterEvaluation> Evaluate(Guid storeId, Guid campaignId, string wbId, int? status,
        IReadOnlyList<Nomenclature> catalog, IReadOnlyList<CampaignNomenclatureStatistics> articles,
        IReadOnlyList<CampaignStatistics> campaignRows, IReadOnlyList<WbCampaignDailyCheck> checks,
        IReadOnlyList<WbSourceQuality> quality, IReadOnlyList<WbOperationalSnapshot> snapshots,
        WbAdvertisingSettings? settings, WbStoreNorms? storeNorm, WbCampaignNorms? campaignNorm,
        IReadOnlyList<ClusterEvaluation> clusters,
        RecommendationWindow window, DateTime now, bool stockEnabled = false)
    {
        var result = new List<ClusterEvaluation>();
        var today = WbSyncPlanner.Today(now);
        var spendWindow = RecommendationPeriods.Recent(today, 3);
        var budgetWindow = RecommendationPeriods.Recent(today, 7);
        WbOperationalSnapshot? Source(string name)
        {
            var ordered = snapshots.Where(x => x.Source == name && (name != "budgets" || x.EntityKey == wbId))
                .OrderByDescending(x => x.RequestedAtUtc).ThenByDescending(x => x.ReceivedAtUtc).ToArray();
            var last = ordered.FirstOrDefault();
            // A replacement still being generated does not invalidate a recent complete snapshot.
            // A failed attempt does invalidate it; never skip over an error to an older success.
            var completed = ordered.FirstOrDefault(x => x.State != "collecting");
            return last?.State == "collecting" && completed?.State == "ready" ? completed : last;
        }
        SourceEvidence Evidence(string name)
        {
            var s = Source(name);
            return new(s?.Id, name, s?.State ?? "unknown", s?.CoverageStartedAtUtc ?? s?.RequestedAtUtc, s?.ReceivedAtUtc, s?.ErrorCode);
        }
        T? Payload<T>(string name)
        {
            try { var s = Source(name); return s?.State == "ready" ? JsonSerializer.Deserialize<T>(s.PayloadJson, Json) : default; }
            catch (JsonException) { return default; }
        }
        bool Day(DateOnly day)
        {
            var check = checks.SingleOrDefault(x => x.Date == day);
            var q = quality.Where(x => x.Source == "fullstats" && (x.Entity == campaignId.ToString() || x.Entity == "") && x.StartDate <= day && x.EndDate >= day)
                .OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefault();
            return check?.Result is "data" or "zero" && !(check.LastAttemptResult == "no_data" && check.LastAttemptAtUtc >= check.CheckedAtUtc) &&
                (q == null || WbQuality.Confirmed(q.State));
        }
        bool Complete(RecommendationWindow w) => Enumerable.Range(0, w.Days).All(i => Day(w.StartDate.AddDays(i)));
        CampaignNomenclatureStatistics[] Basis(Guid article, RecommendationWindow w) => articles.Where(x => x.NomenclatureId == article &&
            DateOnly.FromDateTime(x.Date) >= w.StartDate && DateOnly.FromDateTime(x.Date) <= w.EndDate).ToArray();
        bool ArticleComplete(Guid article, RecommendationWindow w)
        {
            var dates = Basis(article, w).Select(x => DateOnly.FromDateTime(x.Date)).ToHashSet();
            return Complete(w) && Enumerable.Range(0, w.Days).All(i => dates.Contains(w.StartDate.AddDays(i)) ||
                checks.Any(x => x.Date == w.StartDate.AddDays(i) && x.Result == "zero" && x.Spend == 0));
        }
        WbCampaignInfo? metadata = Payload<WbCampaignInfo[]>("advertising")?.FirstOrDefault(x => x.Id.ToString() == wbId);
        var metadataEvidence = Evidence("advertising");
        // Read the metadata cached by the existing import only if it is still recent.
        if (settings != null)
        {
            try
            {
                var p = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(settings.PayloadJson, Json);
                if (p?.Campaign is not null && p.CampaignReceivedAtUtc > (Source("advertising")?.RequestedAtUtc ?? DateTime.MinValue))
                {
                    metadata = p.Campaign;
                    metadataEvidence = new(null, "advertising", "ready", p.CampaignReceivedAtUtc, p.CampaignReceivedAtUtc);
                }
            }
            catch (JsonException) { }
        }
        var membershipFresh = metadata != null && metadataEvidence.Fresh(now);
        var members = (metadata?.NomenclatureIds ?? []).Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal);
        var known = catalog.Where(x => x.StoreId == storeId && (members.Contains(x.WbNomenclatureId) || articles.Any(a => a.NomenclatureId == x.Id)))
            .OrderBy(x => x.WbNomenclatureId, StringComparer.Ordinal).ToArray();
        var mode = campaignNorm?.RecommendationMode ?? "profit";
        var norm = RecommendationEngine.ResolveNorm(campaignNorm?.TargetDrr, storeNorm?.TargetDrr,
            campaignVersion: campaignNorm?.Version ?? 0, storeVersion: storeNorm?.Version ?? 0);

        ClusterEvaluation Build(string rule, int group, string state, string reason, string text, RecommendationSubject subject,
            RecommendationFacts facts, RecommendationNorm n, RecommendationPresentation presentation, bool complete, decimal? threshold)
        {
            var q = new RecommendationQuality(complete, true, complete, true);
            return new(Guid.Empty, subject, facts, n, q, RecommendationEngine.Economy(facts, n, .2m),
                [new(rule, 3, group, true, state, reason, text, threshold, presentation.Primary.Kind)], presentation);
        }
        var stockFbw = stockEnabled ? Payload<StockPosition[]>("stocks_fbw") : null;
        var stockFbs = stockEnabled ? Payload<StockPosition[]>("stocks_fbs") : null;
        var demand = stockEnabled ? Payload<DemandPayload>("demand") : null;
        foreach (var article in known)
        {
            var subject = new RecommendationSubject(storeId, campaignId, article.Id, article.WbNomenclatureId, article.Name, null, "article");
            var recent = Basis(article.Id, spendWindow);
            var availableDays = Enumerable.Range(0, window.Days).Select(i => window.StartDate.AddDays(i)).Where(Day).ToHashSet();
            var current = Basis(article.Id, window).Where(x => availableDays.Contains(DateOnly.FromDateTime(x.Date))).ToArray();
            var currentVerified = availableDays.Count > 0 && current.Length > 0 && current.All(x => x.RecommendationFieldsVerified == true && x.Revenue >= 0);
            var facts = new RecommendationFacts(current.Sum(x => x.Spend), current.Sum(x => x.Impressions), current.Sum(x => x.Clicks),
                current.Sum(x => x.Carts), current.Sum(x => x.Orders), current.All(x => x.OrderedProducts.HasValue) ? current.Sum(x => x.OrderedProducts) : null,
                current.Sum(x => x.OrderedProducts) > 0 ? current.Sum(x => x.Revenue) / current.Sum(x => x.OrderedProducts) : null);
            var bid = metadata?.ArticleBids?.FirstOrDefault(x => x.NmId.ToString() == article.WbNomenclatureId);
            var pointBidAvailable = bid?.SearchRubles > 0 || bid?.RecommendationRubles > 0;
            decimal? currentBid = bid?.SearchRubles == bid?.RecommendationRubles ? bid?.SearchRubles :
                bid?.SearchRubles is null ? bid?.RecommendationRubles : bid?.RecommendationRubles is null ? bid?.SearchRubles : null;
            var inCampaign = membershipFresh && members.Contains(article.WbNomenclatureId);
            if (stockEnabled)
            {
            var stockSources = new[] { Evidence("stocks_fbw"), Evidence("stocks_fbs"), Evidence("demand"), metadataEvidence };
            decimal? fbw = stockFbw == null ? null : stockFbw.Where(x => x.NmId.ToString() == article.WbNomenclatureId).Sum(x => x.Quantity);
            decimal? fbs = stockFbs == null ? null : stockFbs.Where(x => x.NmId.ToString() == article.WbNomenclatureId).Sum(x => x.Quantity);
            var demandValid = demand != null && demand.Window == window;
            // A missing product in the sales funnel is not evidence of zero orders.
            var units = demandValid ? demand!.Items.FirstOrDefault(x => x.NmId.ToString() == article.WbNomenclatureId)?.Units : null;
            var recentSpend = recent.Sum(x => x.Spend);
            var stock = OperationalRules.Stock(fbw, fbs, units, recentSpend, ArticleComplete(article.Id, spendWindow),
                stockSources.Take(2).All(x => x.Fresh(now)) && inCampaign, mode,
                metadata?.NomenclatureIds.Count == 1, metadata?.CanChangeArticles == true,
                pointBidAvailable);
            var stockAction = stock.Action.Kind == "reduce_article_bid" ? OperationalRules.Reduce(30, currentBid, null, null, "article") : stock.Action;
            var stockText = stock.Reason switch
            {
                "out_of_stock" => "Доступный остаток FBW/FBO + FBS равен нулю, реклама продолжает расходовать бюджет.",
                "stock_below_3_days" => "По темпу заказанных единиц запаса осталось меньше трёх дней.",
                "stock_below_7_days" => "По темпу заказанных единиц запаса осталось меньше семи дней.",
                "stocks_incomplete" => "Нет актуальных полных данных: " + string.Join("; ",
                    stockSources.Where(x => x.Source != "demand" && !x.Fresh(now)).Select(x => $"{x.Source}: {x.State}" +
                        (x.ErrorCode == "http_403" ? " — WB запретил доступ; проверьте тип и права токена" : x.ErrorCode != null ? $" ({x.ErrorCode})" : " — снимок отсутствует или старше часа"))) +
                    (!ArticleComplete(article.Id, spendWindow) ? "; не подтверждён рекламный расход за все три дня" : "") + (!inCampaign ? "; товар не подтверждён в текущем составе кампании" : ""),
                "demand_incomplete" => "Нет подтверждённого количества заказанных единиц по всем каналам за 14 дней.",
                "zero_demand" => "За 14 дней нет заказанных единиц; срок запаса не рассчитывается.",
                _ => "Условия предупреждения об остатках не выполнены."
            };
            var stockDetails = new OperationalDetails("stock", spendWindow, stockSources, fbw, fbs, stock.StockDays,
                recentSpend / 3, units / 14m, BlockGrowth: stock.BlockGrowth);
            var stockPresentation = new RecommendationPresentation(stockAction, [], null, false, recentSpend / 3 * 7,
                stock.Status == "skipped" ? [new(stock.Reason, stockText)] : [], [], stock.Warning, Details: stockDetails, Priority: stock.Priority);
            result.Add(Build("R01", 1, stock.Status, stock.Reason, stockText, subject,
                facts with { Spend = recentSpend }, norm, stockPresentation, stock.Status != "skipped", 7));
            }

            if (metadata?.BidType == "manual") continue;
            var revenue = current.Sum(x => x.Revenue);
            var drr = revenue > 0 ? facts.Spend / revenue : (decimal?)null;
            var threshold = norm.Value * (mode == "growth" ? 1.5m : 1.2m);
            var protectedLaunch = RecommendationActions.LaunchProtected(campaignNorm?.LaunchDate, window, today);
            var state = !currentVerified || !inCampaign || protectedLaunch || metadata?.BidType != "unified" ? "skipped" :
                facts.Orders >= 3 && drr > threshold ? "triggered" : "not_triggered";
            var restrictions = new List<RecommendationRestriction>();
            if (!currentVerified) restrictions.Add(new("incomplete", "Нет проверенной товарной статистики за доступные дни."));
            else if (availableDays.Count < window.Days) restrictions.Add(new("incomplete", $"Предварительно: расчёт по {availableDays.Count} из {window.Days} дней."));
            if (!inCampaign) restrictions.Add(new("membership", "Состав кампании или настройки ставки старше часа либо неизвестны."));
            if (metadata?.BidType != "unified") restrictions.Add(new("bid_type", "Единая ставка не подтверждена настройками WB."));
            if (protectedLaunch) restrictions.Add(new("launch", "Защита запуска: снижение ставки не предлагается."));
            var blockedExclusions = clusters.Any(x => x.Subject.ArticleId == article.Id && x.Rules.Any(r => r.Status == "triggered" && r.RuleId is "R03" or "R06") &&
                x.Presentation?.Primary.Kind == "exclude" && x.Presentation.Control?.Excluded != true);
            var action = new RecommendationAction("check_product", "Проверьте цену и карточку товара", Level: "article");
            if (blockedExclusions) restrictions.Add(new("exclude_first", "Сначала исключите проблемные кластеры. Решение «Принял» не подтверждает изменение в WB."));
            else if (pointBidAvailable && state == "triggered")
                action = OperationalRules.Reduce(drr > 0 ? (1 - norm.Value / drr.Value) * 100 : 0, currentBid, null, null, "article");
            else if (state == "triggered") restrictions.Add(new("scope_unknown", "Точечная ставка товара не подтверждена. Общее снижение может затронуть эффективные товары; оно не предлагается."));
            var details = new OperationalDetails("high_article_drr", window, [metadataEvidence], AffectedArticles: [article.WbNomenclatureId]);
            var presentation = new RecommendationPresentation(action, [], null, false, facts.Spend / Math.Max(1, availableDays.Count) * 7, restrictions, [],
                "ДРР товара рассчитан по фактической рекламной сумме заказов; минимум задан числом заказов O.", Details: details);
            var evaluation = Build("R07", 3, state, protectedLaunch ? "launch" : !currentVerified ? "incomplete_data" : !inCampaign ? "membership" : "article_high_drr",
                state == "skipped" ? string.Join(" ", restrictions.Select(x => x.Label)) : state == "triggered" ? "ДРР товара при единой ставке превышает порог режима." : "Проверка ДРР товара при единой ставке.",
                subject, facts, norm, presentation, currentVerified && inCampaign, threshold);
            result.Add(evaluation with { Economy = new(revenue, drr, null, null, null, null),
                Quality = new(availableDays.Count == window.Days, true, currentVerified, true,
                    AvailableDays: availableDays.Count, ExpectedDays: window.Days) });
        }
        var budgets = Payload<Dictionary<string, decimal>>("budgets");
        var balance = Payload<Dictionary<string, decimal>>("balance");
        var budget = budgets != null && budgets.TryGetValue(wbId, out var b) ? b : (decimal?)null;
        var funds = balance != null && balance.TryGetValue("amount", out var amount) ? amount : (decimal?)null;
        var campaignBasis = campaignRows.Where(x => DateOnly.FromDateTime(x.Date) >= budgetWindow.StartDate && DateOnly.FromDateTime(x.Date) <= budgetWindow.EndDate).ToArray();
        var campaignDates = campaignBasis.Select(x => DateOnly.FromDateTime(x.Date)).ToHashSet();
        var fullCampaign = Complete(budgetWindow) && Enumerable.Range(0, 7).All(i => campaignDates.Contains(budgetWindow.StartDate.AddDays(i)) ||
            checks.Any(x => x.Date == budgetWindow.StartDate.AddDays(i) && x.Result == "zero" && x.Spend == 0));
        var spend = campaignBasis.Sum(x => x.Spend); var campaignRevenue = campaignBasis.Sum(x => x.Revenue);
        var budgetSources = new[] { Evidence("budgets"), Evidence("balance"), metadataEvidence };
        var budgetSignal = OperationalRules.Budget(spend, campaignRevenue, norm.Value, budget, funds, fullCampaign,
            Evidence("budgets").Fresh(now) && metadataEvidence.Fresh(now), metadata?.Status == 9);
        var warning = budgetSignal.InsufficientBalance ? "На общем рекламном счёте недостаточно средств: сначала пополните счёт." :
            !Evidence("balance").Fresh(now) ? "Баланс рекламного счёта неизвестен или устарел; проверьте возможность пополнения." : null;
        var budgetAction = new RecommendationAction("top_up_budget", $"Пополните бюджет на {budgetSignal.TopUp:0.##} ₽", Level: "campaign");
        var budgetRestriction = "Нет подтверждения: " + string.Join("; ", budgetSources.Where(x => x.Source != "balance" && !x.Fresh(now))
            .Select(x => $"{x.Source}: {x.State}" + (x.ErrorCode != null ? $" ({x.ErrorCode})" : " — снимок отсутствует или старше часа"))) +
            (!fullCampaign ? "; не все семь дней рекламной статистики загружены" : "") + (budget == null ? "; бюджет кампании не получен" : "");
        var budgetPresentation = new RecommendationPresentation(budgetSignal.Status == "triggered" ? budgetAction : new("watch", "Дождитесь подтверждённых данных", Level: "campaign"),
            [], null, false, spend, budgetSignal.Status == "skipped" ? [new("budget_incomplete", budgetRestriction)] : [], [], warning,
            Details: new("budget", budgetWindow, budgetSources, DailySpend: spend / 7, DailyUnits: campaignBasis.Sum(x => x.Orders) / 7m,
                Budget: budget, BudgetDays: budgetSignal.Days, TopUp: budgetSignal.TopUp, Balance: Evidence("balance").Fresh(now) ? funds : null), Priority: 2);
        var campaignFacts = new RecommendationFacts(spend, campaignBasis.Sum(x => x.Impressions), campaignBasis.Sum(x => x.Clicks),
            campaignBasis.Sum(x => x.Carts), campaignBasis.Sum(x => x.Orders), null, null);
        var budgetEvaluation = Build("R02", 1, budgetSignal.Status, budgetSignal.Reason,
            budgetSignal.Status == "skipped" ? budgetRestriction : budgetSignal.Status == "triggered" ? "Эффективной кампании осталось меньше дня бюджета." : "Проверка бюджета кампании.",
            new(storeId, campaignId, null, null, null, null, "campaign"), campaignFacts, norm, budgetPresentation, fullCampaign, 1);
        result.Add(budgetEvaluation with { Economy = new(campaignRevenue, campaignRevenue > 0 ? spend / campaignRevenue : null, null, null, null, null) });
        return result;
    }
}

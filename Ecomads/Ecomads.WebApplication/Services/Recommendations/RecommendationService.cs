using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record RecommendationSubject(Guid StoreId, Guid CampaignId, Guid ArticleId,
    string NomenclatureId, string NomenclatureName, string ClusterName);
public sealed record ClusterEvaluation(Guid EvaluationId, RecommendationSubject Subject,
    RecommendationFacts Facts, RecommendationNorm Norm, RecommendationQuality Quality,
    RecommendationEconomy Economy, IReadOnlyList<RuleEvaluation> Rules, RecommendationPresentation? Presentation = null);
public sealed record RecommendationCard(Guid EvaluationId, RecommendationSubject Subject,
    RuleEvaluation Rule, RecommendationFacts Facts, RecommendationNorm Norm, RecommendationEconomy Economy,
    bool Preliminary, RecommendationPresentation? Presentation = null, RecommendationDecisionState? Decision = null);
public sealed record RecommendationCoverageSummary(int Enabled, int Evaluated, int Triggered, int Skipped);
public sealed record RecommendationResponse(bool Enabled, bool IsWbConnected, Guid? RunId, int SchemaVersion,
    RecommendationWindow Window, DateTime? CalculatedAtUtc, string Status, string Message,
    IReadOnlyList<RecommendationCard> Cards, IReadOnlyList<ClusterEvaluation> Evaluations,
    IReadOnlyList<WbCampaignClustersController.ClusterRow> Rows, RecommendationCoverageSummary Coverage,
    IReadOnlyList<RecommendationPackage>? Packages = null, Guid? CampaignId = null, string? CampaignName = null);

public sealed class RecommendationService(EcomadsDbContext db, IOptions<RecommendationOptions> options,
    ILogger<RecommendationService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record RawCluster(WbClusterStatistic Stat, Nomenclature Article);
    private sealed record Batch(IReadOnlyList<RawCluster> Raw, IReadOnlyList<CampaignNomenclatureStatistics> Articles,
        IReadOnlyList<WbCampaignDailyCheck> Checks, IReadOnlyList<WbSyncJob> Jobs, IReadOnlyList<WbStoreNorms> Stores,
        IReadOnlyList<WbCampaignNorms> Campaigns, IReadOnlyList<WbArticleNorm> Norms, IReadOnlyList<WbAdvertisingSettings> Controls,
        IReadOnlyList<WbSourceQuality> SourceQuality);

    private async Task<Batch> LoadAsync(Guid[] campaignIds, Guid[] storeIds, RecommendationWindow window, CancellationToken ct)
    {
        var begin = window.StartDate.AddDays(-window.Days);
        var from = begin.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = window.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var raw = await (from stat in db.WbClusterStatistics.AsNoTracking()
            join article in db.Nomenclatures.AsNoTracking() on stat.NomenclatureId equals article.Id
            join campaign in db.Campaigns.AsNoTracking() on stat.CampaignId equals campaign.Id
            where campaignIds.Contains(stat.CampaignId) && article.StoreId == campaign.StoreId && stat.Date >= begin && stat.Date <= window.EndDate
            orderby stat.NomenclatureId, stat.ClusterName, stat.Date select new RawCluster(stat, article)).ToListAsync(ct);
        return new(raw, await db.CampaignNomenclatureStatistics.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId) && x.Date >= from && x.Date <= to).ToListAsync(ct),
            await db.WbCampaignDailyChecks.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId) && x.Date >= begin && x.Date <= window.EndDate).ToListAsync(ct),
            await db.WbSyncJobs.AsNoTracking().Where(x => storeIds.Contains(x.StoreId) && x.Kind == "clusters" && x.Status == "completed" && x.StartDate <= window.EndDate && x.EndDate >= begin).ToListAsync(ct),
            await db.WbStoreNorms.AsNoTracking().Where(x => storeIds.Contains(x.StoreId)).ToListAsync(ct),
            await db.WbCampaignNorms.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId)).ToListAsync(ct),
            await db.WbArticleNorms.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId)).ToListAsync(ct),
            await db.WbAdvertisingSettings.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId)).ToListAsync(ct),
            await db.WbSourceQualities.AsNoTracking().Where(x => storeIds.Contains(x.StoreId) &&
                x.StartDate <= window.EndDate && x.EndDate >= begin).ToListAsync(ct));
    }

    public async Task<IReadOnlyList<RecommendationResponse>> CalculateManyAsync(Guid sellerId, Guid? storeId,
        RecommendationWindow window, CancellationToken ct)
    {
        if (!window.IsValid) throw new ArgumentException("Выберите период от 1 до 366 дней.");
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.Store.SellerId == sellerId &&
            (storeId == null || x.StoreId == storeId)).Select(x => new { x.Id, x.StoreId }).ToListAsync(ct);
        var batch = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var loaded = await LoadAsync(campaigns.Select(x => x.Id).ToArray(), campaigns.Select(x => x.StoreId).Distinct().ToArray(), window, ct);
            await transaction.CommitAsync(ct);
            return loaded;
        });
        var reports = new List<RecommendationResponse>();
        foreach (var campaign in campaigns.OrderBy(x => x.Id))
        {
            var report = await CalculateAsync(sellerId, campaign.Id, window, ct, batch);
            if (report is not null) reports.Add(report);
        }
        return reports;
    }

    public async Task<RecommendationResponse?> CalculateAsync(Guid sellerId, Guid campaignId,
        RecommendationWindow window, CancellationToken ct) => await CalculateAsync(sellerId, campaignId, window, ct, null);

    private async Task<RecommendationResponse?> CalculateAsync(Guid sellerId, Guid campaignId,
        RecommendationWindow window, CancellationToken ct, Batch? batch)
    {
        if (!window.IsValid) throw new ArgumentException("Выберите период от 1 до 366 дней.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var campaign = await db.Campaigns.AsNoTracking().Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
                .Select(x => new { x.StoreId, x.WbCampaignId, x.Name, Connected = x.Store.ApiKey != null }).SingleOrDefaultAsync(ct);
            if (campaign == null) return null;
            var settings = options.Value;
            RecommendationResponse Empty(bool enabled, string status, string message) => new(enabled,
                campaign.Connected, null, 2, window, null, status, message, [], [], [], new(0, 0, 0, 0), [], campaignId, campaign.Name);
            if (!settings.Enabled) return Empty(false, "disabled", "Расчёт рекомендаций отключён в настройках сервиса.");
            if (!campaign.Connected) return Empty(true, "not_connected", "Подключите кабинет WB.");

            batch ??= await LoadAsync([campaignId], [campaign.StoreId], window, ct);
            bool SourceConfirmed(string kind, string entity, DateOnly start, DateOnly end)
            {
                for (var day = start; day <= end; day = day.AddDays(1))
                {
                    var latest = batch.SourceQuality.Where(x => x.StoreId == campaign.StoreId && x.Source == kind &&
                        (x.Entity == "" || x.Entity == entity) && x.StartDate <= day && x.EndDate >= day)
                        .OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefault();
                    if (latest == null && kind == "clusters") return false;
                    if (latest != null && !WbQuality.Confirmed(latest.State)) return false;
                }
                return true;
            }
            var raw = batch.Raw.Where(x => x.Stat.CampaignId == campaignId && x.Stat.Date >= window.StartDate).ToList();
            var articleRows = batch.Articles.Where(x => x.CampaignId == campaignId && DateOnly.FromDateTime(x.Date) >= window.StartDate).OrderBy(x => x.NomenclatureId).ThenBy(x => x.Date).ToList();
            var checks = batch.Checks.Where(x => x.CampaignId == campaignId && x.Date >= window.StartDate).OrderBy(x => x.Date).ToList();
            var checkedDays = checks.Where(x => x.Result is "data" or "zero" &&
                !(x.LastAttemptResult == "no_data" && x.LastAttemptAtUtc >= x.CheckedAtUtc) &&
                SourceConfirmed("fullstats", campaignId.ToString(), x.Date, x.Date)).Select(x => x.Date).ToHashSet();
            var fullComplete = Enumerable.Range(0, window.Days).All(i => checkedDays.Contains(window.StartDate.AddDays(i)));
            var jobs = batch.Jobs.Where(x => x.StoreId == campaign.StoreId).OrderBy(x => x.StartDate).ThenBy(x => x.Id).ToList();
            var intervals = jobs.Select(x => new ClusterCoverageInterval(x.StartDate, x.EndDate,
                JsonSerializer.Deserialize<WbNormQueryPair[]>(x.PairIdsJson ?? "[]") ?? [])).ToArray();
            var storeNorm = batch.Stores.SingleOrDefault(x => x.StoreId == campaign.StoreId);
            var campaignNorm = batch.Campaigns.SingleOrDefault(x => x.CampaignId == campaignId);
            var articleNorms = batch.Norms.Where(x => x.CampaignId == campaignId).ToDictionary(x => x.NomenclatureId);
            var advertising = batch.Controls.SingleOrDefault(x => x.CampaignId == campaignId);
            var today = WbSyncPlanner.Today();
            var basisByArticle = articleRows.ToLookup(x => x.NomenclatureId);
            var evaluations = new List<ClusterEvaluation>();
            var table = new List<WbCampaignClustersController.ClusterRow>();
            foreach (var group in raw.GroupBy(x => new { x.Article.Id, x.Article.WbNomenclatureId,
                         x.Article.Name, x.Stat.ClusterName }))
            {
                var basis = basisByArticle[group.Key.Id].ToArray();
                var stat = group.Select(x => x.Stat).ToArray();
                int? Sum(Func<WbClusterStatistic, int?> field) => stat.All(x => field(x).HasValue)
                    ? stat.Sum(x => field(x)!.Value) : null;
                // Missing article rows on a positively spending day cannot silently become zero.
                var articleDates = basis.Select(x => DateOnly.FromDateTime(x.Date)).ToHashSet();
                var articleComplete = fullComplete && checks.All(x => articleDates.Contains(x.Date) ||
                    x.Result == "zero" && x.Spend == 0);
                var fieldsVerified = basis.Length > 0 && basis.All(x => x.RecommendationFieldsVerified == true && x.Revenue >= 0);
                var pairComplete = long.TryParse(campaign.WbCampaignId, out var advertId) &&
                    long.TryParse(group.Key.WbNomenclatureId, out var nmId) && RecommendationCoverage.Covers(window, advertId, nmId, intervals) &&
                    SourceConfirmed("clusters", $"{campaignId}:{group.Key.Id}", window.StartDate, window.EndDate);
                var price = WbClusterOrderEstimateCalculator.CalculateExact(window.StartDate, window.EndDate,
                    basis, stat, articleComplete && fieldsVerified, pairComplete, true).AverageOrderedUnitPrice;
                var quality = new RecommendationQuality(articleComplete, pairComplete, fieldsVerified,
                    settings.SourcesComparable, window.EndDate >= WbSyncPlanner.Today() ? "open_day" : "unverified");
                articleNorms.TryGetValue(group.Key.Id, out var articleNorm);
                var norm = RecommendationEngine.ResolveNorm(articleNorm?.TargetDrr, campaignNorm?.TargetDrr, storeNorm?.TargetDrr,
                    articleNorm?.Version ?? 0, campaignNorm?.Version ?? 0, storeNorm?.Version ?? 0);
                var facts = new RecommendationFacts(stat.Sum(x => x.Spend), Sum(x => x.Views), Sum(x => x.Clicks),
                    Sum(x => x.Carts), Sum(x => x.Orders), Sum(x => x.OrderedProducts), price);
                var economy = RecommendationEngine.Economy(facts, norm, settings.PriceScenario);
                var rules = RecommendationEngine.Evaluate(facts, norm, quality, settings);
                var gaps = new List<string>();
                if (!articleComplete) gaps.Add("Не подтверждены все дни товарной статистики WB за выбранный период.");
                if (!pairComplete) gaps.Add("Кластеры за выбранный период ещё не загружены полностью; ожидается фоновая загрузка.");
                if (!fieldsVerified) gaps.Add("Старые товарные строки требуют повторной загрузки для проверки суммы заказов и заказанных единиц.");
                if (gaps.Count > 0) rules = rules.Select(r => r.Status == "skipped" ? r with { Explanation = string.Join(" ", gaps) } : r).ToArray();
                var launch = articleNorm?.LaunchDate ?? campaignNorm?.LaunchDate;
                if (RecommendationActions.LaunchProtected(launch, window, today))
                    rules = rules.Select(x => x.Enabled ? x with { Status = "skipped", ReasonCode = "launch", Explanation = "Защита запуска: выбранный период пересекает первые 21 день или запуск ещё продолжается." } : x).ToArray();
                int? articleUnits = basis.Length > 0 && basis.All(x => x.OrderedProducts.HasValue) && articleComplete && fieldsVerified
                    ? basis.Sum(x => x.OrderedProducts!.Value) : null;
                var control = AdvertisingSettingsPayload.Control(advertising, group.Key.WbNomenclatureId, group.Key.ClusterName, DateTime.UtcNow);
                var presentation = RecommendationActions.Select(rules.FirstOrDefault(x => x.Status == "triggered") ?? rules[0],
                    facts, norm, quality, economy, control, articleUnits, launch, window, today);
                var subject = new RecommendationSubject(campaign.StoreId, campaignId, group.Key.Id,
                    group.Key.WbNomenclatureId, group.Key.Name, group.Key.ClusterName);
                evaluations.Add(new(Guid.Empty, subject, facts, norm, quality, economy, rules, presentation));
                var reason = rules.FirstOrDefault(x => x.Status == "skipped")?.Explanation;
                table.Add(new(group.Key.WbNomenclatureId, group.Key.Name, group.Key.ClusterName, facts.Spend,
                    facts.Views, facts.Clicks, facts.Carts, facts.Orders, facts.Clicks > 0 ? facts.Spend / facts.Clicks : null,
                    rules.Any(x => x.Status == "triggered") ? (presentation.IsKey ? "Ключевой · " : "") + presentation.Primary.Label : reason ??
                    (rules.Any(x => x.ReasonCode == "low_sample") ? "Мало наблюдений для высокого ДРР" : "По проверенным правилам отклонений нет"),
                    facts.OrderedProducts, price, economy.EstimatedOrderSum, economy.Drr * 100,
                    reason == null ? "preliminary" : "unavailable", reason ?? "Оценка; зрелость статистики WB пока не подтверждена."));
            }
            var thresholds = new DiagnosticThresholds(campaignNorm?.MinClicks ?? storeNorm?.MinClicks ?? 30,
                campaignNorm?.MinViews ?? storeNorm?.MinViews ?? 1000, campaignNorm?.MinCarts ?? storeNorm?.MinCarts ?? 20,
                campaignNorm?.MinOrders ?? storeNorm?.MinOrders ?? 3, campaignNorm?.MinSpend ?? storeNorm?.MinSpend ?? 500,
                (campaignNorm?.MinCtr ?? storeNorm?.MinCtr) / 100,
                (campaignNorm?.CartConversion ?? storeNorm?.CartConversion) / 100,
                (campaignNorm?.OrderCartConversion ?? storeNorm?.OrderCartConversion) / 100,
                (campaignNorm?.OrderClickConversion ?? storeNorm?.OrderClickConversion) / 100);
            var priorWindow = new RecommendationWindow(window.StartDate.AddDays(-window.Days), window.StartDate.AddDays(-1));
            var priorChecks = batch.Checks.Where(x => x.CampaignId == campaignId && x.Date < window.StartDate).ToArray();
            var priorComplete = Enumerable.Range(0, priorWindow.Days).All(i => priorChecks.Any(x => x.Date == priorWindow.StartDate.AddDays(i) &&
                x.Result is "data" or "zero" && !(x.LastAttemptResult == "no_data" && x.LastAttemptAtUtc >= x.CheckedAtUtc) &&
                SourceConfirmed("fullstats", campaignId.ToString(), x.Date, x.Date)));
            evaluations = evaluations.Select(e =>
            {
                var previousRows = batch.Raw.Where(x => x.Stat.CampaignId == campaignId && x.Article.Id == e.Subject.ArticleId &&
                    x.Stat.ClusterName == e.Subject.ClusterName && x.Stat.Date < window.StartDate).Select(x => x.Stat).ToArray();
                var priorBasis = batch.Articles.Where(x => x.CampaignId == campaignId && x.NomenclatureId == e.Subject.ArticleId && DateOnly.FromDateTime(x.Date) < window.StartDate).ToArray();
                var basisDates = priorBasis.Select(x => DateOnly.FromDateTime(x.Date)).ToHashSet();
                var verified = priorBasis.Length > 0 && priorBasis.All(x => x.RecommendationFieldsVerified == true && x.Revenue >= 0) &&
                    priorChecks.All(x => basisDates.Contains(x.Date) || x.Result == "zero" && x.Spend == 0);
                var covered = long.TryParse(campaign.WbCampaignId, out var advert) && long.TryParse(e.Subject.NomenclatureId, out var nm) && RecommendationCoverage.Covers(priorWindow, advert, nm, intervals) &&
                    SourceConfirmed("clusters", $"{campaignId}:{e.Subject.ArticleId}", priorWindow.StartDate, priorWindow.EndDate);
                RecommendationFacts? previous = null;
                if (priorComplete && verified && covered && previousRows.Length > 0)
                {
                    int? Sum(Func<WbClusterStatistic, int?> f) => previousRows.All(x => f(x).HasValue) ? previousRows.Sum(x => f(x)!.Value) : null;
                    var price = WbClusterOrderEstimateCalculator.CalculateExact(priorWindow.StartDate, priorWindow.EndDate, priorBasis, previousRows, true, true, true).AverageOrderedUnitPrice;
                    previous = new(previousRows.Sum(x => x.Spend), Sum(x => x.Views), Sum(x => x.Clicks), Sum(x => x.Carts), Sum(x => x.Orders), Sum(x => x.OrderedProducts), price);
                }
                var peers = evaluations.Where(x => x.Subject.ArticleId == e.Subject.ArticleId && x.Subject.ClusterName != e.Subject.ClusterName && x.Quality.Complete && x.Quality.FieldsVerified).Select(x => x.Facts).ToArray();
                var diagnostics = RecommendationDiagnostics.Calculate(e.Facts, e.Economy, e.Norm, peers, previous,
                    previous is null ? null : RecommendationEngine.Economy(previous, e.Norm, settings.PriceScenario), thresholds);
                return e with { Presentation = e.Presentation! with { Diagnostics = diagnostics } };
            }).ToList();
            // Hash includes coverage/empty results too, avoiding stale "no data" snapshots after imports.
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                sellerId, campaignId, window, engineVersion = 2,
                settings.PriceScenario, settings.NoUnitsSpendFloor, settings.HighDrrMinimumUnits,
                settings.Rule03Enabled, settings.Rule06Enabled, settings.SourcesComparable,
                checks = checks.Select(x => new { x.Date, x.Result, x.Spend }), intervals,
                storeVersion = storeNorm?.Version, campaignVersion = campaignNorm?.Version,
                evaluations
            }, Json))));
            var existing = await db.WbRecommendationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId &&
                x.StartDate == window.StartDate && x.EndDate == window.EndDate && x.Fingerprint == fingerprint, ct);
            if (existing != null) return await WithDecisionsAsync(JsonSerializer.Deserialize<RecommendationResponse>(existing.SnapshotJson, Json)!, ct);
            var runId = Guid.NewGuid();
            evaluations = evaluations.Select(x => x with { EvaluationId = Guid.NewGuid() }).ToList();
            var cards = evaluations.SelectMany(x => x.Rules.Where(rule => rule.Status == "triggered")
                .Select(rule => new RecommendationCard(x.EvaluationId, x.Subject, rule, x.Facts, x.Norm, x.Economy,
                    window.EndDate > today.AddDays(-2) || !x.Quality.Complete || x.Presentation?.Control?.Status != "ready", x.Presentation)))
                .OrderBy(x => x.Rule.Group).ThenByDescending(x => x.Facts.Spend)
                .ThenBy(x => x.Subject.NomenclatureId, StringComparer.Ordinal).ThenBy(x => x.Subject.ClusterName, StringComparer.Ordinal).ToArray();
            var allRules = evaluations.SelectMany(x => x.Rules).ToArray();
            var coverage = new RecommendationCoverageSummary(allRules.Count(x => x.Enabled),
                allRules.Count(x => x.Status is "triggered" or "not_triggered"), cards.Length,
                allRules.Count(x => x.Status == "skipped"));
            var knownPairs = intervals.SelectMany(x => x.Pairs).Where(x => x.AdvertId.ToString() == campaign.WbCampaignId).Distinct().ToArray();
            var emptyConfirmed = fullComplete && knownPairs.Length > 0 &&
                knownPairs.All(pair => RecommendationCoverage.Covers(window, pair.AdvertId, pair.NmId, intervals));
            var status = raw.Count == 0 ? emptyConfirmed ? "empty" : "not_ready" : coverage.Skipped > 0 ? "limited" :
                coverage.Evaluated == 0 ? "unavailable" : cards.Length > 0 ? "preliminary" :
                allRules.Any(x => x.Enabled && x.ReasonCode == "low_sample") ? "low_sample" : "no_deviations";
            var message = status switch
            {
                "empty" => "За выбранный период в загруженной статистике нет поисковых кластеров.",
                "not_ready" => "Данные за выбранный период ещё не готовы. Проверьте загрузку статистики.",
                "limited" => "Часть объектов не прошла проверку данных. Причины указаны отдельно.",
                "unavailable" => "Включённые правила недоступны для проверки.",
                "low_sample" => "Для части объектов мало заказанных единиц для проверки высокого ДРР. Порог расхода без результата проверяется отдельно.",
                "no_deviations" => "По проверенным правилам отклонений не найдено. Данные могут уточняться.",
                _ => "Предварительные диагностические рекомендации. Данные WB могут уточняться."
            };
            var response = new RecommendationResponse(true, true, runId, 2, window, DateTime.UtcNow, status, message,
                cards, evaluations, table.OrderByDescending(x => x.Spend).ThenBy(x => x.NomenclatureId, StringComparer.Ordinal)
                    .ThenBy(x => x.ClusterName, StringComparer.Ordinal).ToArray(), coverage, RecommendationActions.Packages(cards), campaignId, campaignNorm?.CustomName ?? campaign.Name);
            var run = new WbRecommendationRun { Id = runId, SellerId = sellerId, StoreId = campaign.StoreId,
                CampaignId = campaignId, StartDate = window.StartDate, EndDate = window.EndDate, Fingerprint = fingerprint,
                SchemaVersion = 2, CalculatedAtUtc = response.CalculatedAtUtc!.Value, SnapshotJson = JsonSerializer.Serialize(response, Json) };
            db.WbRecommendationRuns.Add(run);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" })
            {
                await transaction.RollbackAsync(ct);
                db.Entry(run).State = EntityState.Detached;
                await transaction.DisposeAsync();
                var concurrent = await db.WbRecommendationRuns.AsNoTracking().SingleAsync(x => x.CampaignId == campaignId &&
                    x.StartDate == window.StartDate && x.EndDate == window.EndDate && x.Fingerprint == fingerprint, ct);
                return await WithDecisionsAsync(JsonSerializer.Deserialize<RecommendationResponse>(concurrent.SnapshotJson, Json)!, ct);
            }
            logger.LogInformation("Recommendations {RunId}: evaluated={Evaluated}, triggered={Triggered}, skipped={Skipped}",
                runId, coverage.Evaluated, coverage.Triggered, coverage.Skipped);
            return response;
        });
    }

    public async Task<RecommendationResponse?> SnapshotAsync(Guid sellerId, Guid campaignId, Guid runId, CancellationToken ct)
    {
        var run = await db.WbRecommendationRuns.AsNoTracking().Where(x => x.Id == runId && x.SellerId == sellerId &&
            x.CampaignId == campaignId && db.Campaigns.Any(c => c.Id == x.CampaignId && c.Store.SellerId == sellerId))
            .SingleOrDefaultAsync(ct);
        return run == null || !options.Value.Enabled ? null : await WithDecisionsAsync(JsonSerializer.Deserialize<RecommendationResponse>(run.SnapshotJson, Json)!, ct);
    }

    private async Task<RecommendationResponse> WithDecisionsAsync(RecommendationResponse report, CancellationToken ct)
    {
        var decisions = await db.WbSnapshotDecisions.AsNoTracking().Where(x => x.RunId == report.RunId).ToListAsync(ct);
        return report with { Cards = report.Cards.Select(card =>
        {
            var d = decisions.Where(x => x.EvaluationId == card.EvaluationId && x.RuleId == card.Rule.RuleId).OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
            return card with { Decision = d == null ? null : new(d.Status, d.SnoozeUntil, d.Reason, d.Comment, d.CreatedAtUtc) };
        }).ToArray() };
    }
}

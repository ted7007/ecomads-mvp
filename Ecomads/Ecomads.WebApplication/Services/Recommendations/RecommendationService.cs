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
    RecommendationEconomy Economy, IReadOnlyList<RuleEvaluation> Rules);
public sealed record RecommendationCard(Guid EvaluationId, RecommendationSubject Subject,
    RuleEvaluation Rule, RecommendationFacts Facts, RecommendationNorm Norm, RecommendationEconomy Economy,
    bool Preliminary);
public sealed record RecommendationCoverageSummary(int Enabled, int Evaluated, int Triggered, int Skipped);
public sealed record RecommendationResponse(bool Enabled, bool IsWbConnected, Guid? RunId, int SchemaVersion,
    RecommendationWindow Window, DateTime? CalculatedAtUtc, string Status, string Message,
    IReadOnlyList<RecommendationCard> Cards, IReadOnlyList<ClusterEvaluation> Evaluations,
    IReadOnlyList<WbCampaignClustersController.ClusterRow> Rows, RecommendationCoverageSummary Coverage);

public sealed class RecommendationService(EcomadsDbContext db, IOptions<RecommendationOptions> options,
    ILogger<RecommendationService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RecommendationResponse?> CalculateAsync(Guid sellerId, Guid campaignId,
        RecommendationWindow window, CancellationToken ct)
    {
        if (!window.IsValid) throw new ArgumentException("Выберите период от 1 до 366 дней.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var campaign = await db.Campaigns.AsNoTracking().Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
                .Select(x => new { x.StoreId, x.WbCampaignId, Connected = x.Store.ApiKey != null }).SingleOrDefaultAsync(ct);
            if (campaign == null) return null;
            var settings = options.Value;
            RecommendationResponse Empty(bool enabled, string status, string message) => new(enabled,
                campaign.Connected, null, 1, window, null, status, message, [], [], [], new(0, 0, 0, 0));
            if (!settings.Enabled) return Empty(false, "disabled", "Расчёт рекомендаций отключён в настройках сервиса.");
            if (!campaign.Connected) return Empty(true, "not_connected", "Подключите кабинет WB.");

            var from = DateTime.SpecifyKind(window.StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var to = DateTime.SpecifyKind(window.EndDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var raw = await (from stat in db.WbClusterStatistics.AsNoTracking()
                join article in db.Nomenclatures.AsNoTracking() on stat.NomenclatureId equals article.Id
                where stat.CampaignId == campaignId && article.StoreId == campaign.StoreId &&
                    stat.Date >= window.StartDate && stat.Date <= window.EndDate
                orderby stat.NomenclatureId, stat.ClusterName, stat.Date
                select new { Stat = stat, Article = article }).ToListAsync(ct);
            var articleRows = await db.CampaignNomenclatureStatistics.AsNoTracking()
                .Where(x => x.CampaignId == campaignId && x.Date >= from && x.Date <= to)
                .OrderBy(x => x.NomenclatureId).ThenBy(x => x.Date).ToListAsync(ct);
            var checks = await db.WbCampaignDailyChecks.AsNoTracking().Where(x => x.CampaignId == campaignId &&
                x.Date >= window.StartDate && x.Date <= window.EndDate).OrderBy(x => x.Date).ToListAsync(ct);
            var checkedDays = checks.Where(x => x.Result is "data" or "zero").Select(x => x.Date).ToHashSet();
            var fullComplete = Enumerable.Range(0, window.Days).All(i => checkedDays.Contains(window.StartDate.AddDays(i)));
            var jobs = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == campaign.StoreId &&
                x.Kind == "clusters" && x.Status == "completed" && x.StartDate <= window.EndDate && x.EndDate >= window.StartDate)
                .OrderBy(x => x.StartDate).ThenBy(x => x.Id)
                .Select(x => new { x.StartDate, x.EndDate, x.PairIdsJson }).ToListAsync(ct);
            var intervals = jobs.Select(x => new ClusterCoverageInterval(x.StartDate, x.EndDate,
                JsonSerializer.Deserialize<WbNormQueryPair[]>(x.PairIdsJson ?? "[]") ?? [])).ToArray();
            var storeNorm = await db.WbStoreNorms.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == campaign.StoreId, ct);
            var campaignNorm = await db.WbCampaignNorms.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId, ct);
            var articleNorms = await db.WbArticleNorms.AsNoTracking().Where(x => x.CampaignId == campaignId)
                .ToDictionaryAsync(x => x.NomenclatureId, ct);
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
                    long.TryParse(group.Key.WbNomenclatureId, out var nmId) && RecommendationCoverage.Covers(window, advertId, nmId, intervals);
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
                var subject = new RecommendationSubject(campaign.StoreId, campaignId, group.Key.Id,
                    group.Key.WbNomenclatureId, group.Key.Name, group.Key.ClusterName);
                evaluations.Add(new(Guid.Empty, subject, facts, norm, quality, economy, rules));
                var reason = rules.FirstOrDefault(x => x.Status == "skipped")?.Explanation;
                table.Add(new(group.Key.WbNomenclatureId, group.Key.Name, group.Key.ClusterName, facts.Spend,
                    facts.Views, facts.Clicks, facts.Carts, facts.Orders, facts.Clicks > 0 ? facts.Spend / facts.Clicks : null,
                    rules.Any(x => x.Status == "triggered") ? "Проверить расходы · предварительно" : reason ??
                    (rules.Any(x => x.ReasonCode == "low_sample") ? "Мало наблюдений для высокого ДРР" : "По проверенным правилам отклонений нет"),
                    facts.OrderedProducts, price, economy.EstimatedOrderSum, economy.Drr * 100,
                    reason == null ? "preliminary" : "unavailable", reason ?? "Оценка; зрелость статистики WB пока не подтверждена."));
            }
            // Hash includes coverage/empty results too, avoiding stale "no data" snapshots after imports.
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                sellerId, campaignId, window, engineVersion = RecommendationEngine.Version,
                settings.PriceScenario, settings.NoUnitsSpendFloor, settings.HighDrrMinimumUnits,
                settings.Rule03Enabled, settings.Rule06Enabled, settings.SourcesComparable,
                checks = checks.Select(x => new { x.Date, x.Result, x.Spend }), intervals,
                storeVersion = storeNorm?.Version, campaignVersion = campaignNorm?.Version,
                evaluations
            }, Json))));
            var existing = await db.WbRecommendationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId &&
                x.StartDate == window.StartDate && x.EndDate == window.EndDate && x.Fingerprint == fingerprint, ct);
            if (existing != null) return JsonSerializer.Deserialize<RecommendationResponse>(existing.SnapshotJson, Json)!;
            var runId = Guid.NewGuid();
            evaluations = evaluations.Select(x => x with { EvaluationId = Guid.NewGuid() }).ToList();
            var cards = evaluations.SelectMany(x => x.Rules.Where(rule => rule.Status == "triggered")
                .Select(rule => new RecommendationCard(x.EvaluationId, x.Subject, rule, x.Facts, x.Norm, x.Economy, true)))
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
            var response = new RecommendationResponse(true, true, runId, 1, window, DateTime.UtcNow, status, message,
                cards, evaluations, table.OrderByDescending(x => x.Spend).ThenBy(x => x.NomenclatureId, StringComparer.Ordinal)
                    .ThenBy(x => x.ClusterName, StringComparer.Ordinal).ToArray(), coverage);
            var run = new WbRecommendationRun { Id = runId, SellerId = sellerId, StoreId = campaign.StoreId,
                CampaignId = campaignId, StartDate = window.StartDate, EndDate = window.EndDate, Fingerprint = fingerprint,
                CalculatedAtUtc = response.CalculatedAtUtc!.Value, SnapshotJson = JsonSerializer.Serialize(response, Json) };
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
                return JsonSerializer.Deserialize<RecommendationResponse>(concurrent.SnapshotJson, Json)!;
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
        return run == null || !options.Value.Enabled ? null : JsonSerializer.Deserialize<RecommendationResponse>(run.SnapshotJson, Json);
    }
}

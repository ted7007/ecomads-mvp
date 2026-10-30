using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

/// <summary>Identity and user decisions survive calculation IDs. Exposure snapshots remain immutable.</summary>
public sealed class RecommendationLifecycle(EcomadsDbContext db, TimeProvider clock, ILogger<ResultsService>? logger = null)
{
    public sealed record History(WbResultRecommendation Recommendation, WbResultExposure[] Exposures,
        WbSnapshotDecision[] Decisions)
    {
        public WbSnapshotDecision? Latest => Decisions.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
        private RecommendationCard? card;
        public RecommendationCard Card => card ??= Read(Exposures.OrderByDescending(x => x.ShownAtUtc).First());
    }

    public static string Identity(RecommendationCard card) => JsonSerializer.Serialize(new {
        card.Subject.StoreId, card.Subject.CampaignId, card.Subject.Scope,
        Article = card.Subject.NomenclatureId ?? card.Subject.ArticleId?.ToString(),
        Cluster = NormalizeCluster(card.Subject.ClusterName), card.Rule.RuleId
    });

    public static string? NormalizeCluster(string? value) => value == null ? null :
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    public static RecommendationCard Read(WbResultExposure exposure) =>
        JsonSerializer.Deserialize<RecommendationCard>(exposure.CardJson, ResultsService.Json)!;

    public async Task<History[]> LoadAsync(Guid campaign, CancellationToken ct)
    {
        var recs = await db.WbResultRecommendations.AsNoTracking().Where(x => x.CampaignId == campaign && !x.Legacy).ToArrayAsync(ct);
        var ids = recs.Select(x => x.Id).ToArray();
        var exposures = await db.WbResultExposures.AsNoTracking().Where(x => ids.Contains(x.RecommendationId)).ToArrayAsync(ct);
        var runs = exposures.Select(x => x.RunId).Distinct().ToArray();
        var decisions = await db.WbSnapshotDecisions.AsNoTracking().Where(x => runs.Contains(x.RunId)).ToArrayAsync(ct);
        return recs.Select(r => {
            var shown = exposures.Where(x => x.RecommendationId == r.Id).ToArray();
            return new History(r, shown, decisions.Where(d => shown.Any(e =>
                e.RunId == d.RunId && e.EvaluationId == d.EvaluationId && e.RuleId == d.RuleId)).ToArray());
        }).Where(x => x.Exposures.Length > 0).ToArray();
    }

    public static History? Find(IEnumerable<History> histories, RecommendationCard card) => histories
        .Where(x => Identity(x.Card) == Identity(card)).OrderByDescending(x => x.Recommendation.FirstShownAtUtc)
        .ThenByDescending(x => x.Recommendation.Id).FirstOrDefault();

    public static decimal? WeeklyAmount(RecommendationCard card) => card.Rule.RuleId switch {
        "R03" when card.Facts.OrderedProducts == 0 && card.Economy.AllowedSpendPerUnit.HasValue => card.Presentation?.WeeklySpend,
        "R06" or "R07" when card.Economy.Drr > 0 => card.Presentation?.WeeklySpend * Math.Max(0, 1 - card.Norm.Value / card.Economy.Drr.Value),
        _ => null
    };

    public static bool RefusalExpired(WbSnapshotDecision decision, RecommendationCard rejected,
        RecommendationCard current, DateTime now)
    {
        // Compare the same weekly estimate and rule/norm basis; absent/zero estimates cannot double meaningfully.
        var previous = WeeklyAmount(rejected); var next = WeeklyAmount(current);
        return now >= decision.CreatedAtUtc.AddDays(14) &&
            !current.Preliminary && previous > 0 && next >= previous * 2 &&
            rejected.Rule.RuleVersion == current.Rule.RuleVersion && rejected.Norm.Value == current.Norm.Value;
    }

    public async Task<bool> NewOccurrenceAsync(History history, RecommendationCard card, DateOnly end, CancellationToken ct, DateTime? calculatedAt = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (calculatedAt == null || WbSyncPlanner.Today(calculatedAt.Value) != WbSyncPlanner.Today(now)) return false;
        var rec = history.Recommendation;
        var refusalExpired = false;
        if (history.Latest is { Status: "irrelevant" } refusal && refusal.ReasonCode != CampaignRecommendationSettings.AutoReplenishmentReason)
        {
            var rejected = history.Exposures.Single(e => e.RunId == refusal.RunId && e.EvaluationId == refusal.EvaluationId && e.RuleId == refusal.RuleId);
            refusalExpired = RefusalExpired(refusal, Read(rejected), card, now);
            if (!refusalExpired) return false;
        }
        if (history.Latest is { Status: "snoozed", SnoozeUntil: { } until } && until > WbSyncPlanner.Today(now)) return false;
        if (rec.MeasurementId is { } id)
        {
            var measurement = await db.WbResultMeasurements.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var member = ResultsService.Members(measurement).SingleOrDefault(x => x.RecommendationId == rec.Id);
            if (card.Preliminary || !ResultsService.Actionable(card) || member == null || measurement.Status == "measuring") return false;
            if (member.Card.Rule.RuleId == "R06" && member.Card.Presentation!.Primary.Kind.StartsWith("reduce_") &&
                WbSyncPlanner.Today(now) < WbSyncPlanner.Today(member.Applied.AtUtc).AddDays(7)) return false;
            if (member.Card.Presentation!.Primary.Kind.StartsWith("reduce_") && WbSyncPlanner.Today(now) < measurement.DetectedDate.AddDays(10)) return false;
            var shown = history.Exposures.Single(x => x.Id == member.ExposureId);
            var previousEnd = await db.WbRecommendationRuns.Where(x => x.Id == shown.RunId).Select(x => x.EndDate).SingleAsync(ct);
            if (end <= previousEnd) return false;
            var campaign = await db.Campaigns.AsNoTracking().SingleAsync(x => x.Id == rec.CampaignId, ct);
            if (member.Card.Presentation!.Primary.Kind.StartsWith("reduce_") && ResultsService.Actionable(card) &&
                WbSyncPlanner.Today(now) >= measurement.DetectedDate.AddDays(10))
            {
                var current = (await new ResultObservationReader(db).ReadAsync(campaign, card, now.AddDays(-2), ct, now)).LastOrDefault();
                return current != null && current.AtUtc >= now.AddHours(-24) && ResultObservationReader.Known(card, current);
            }
            var latest = (await new ResultObservationReader(db).ReadAsync(campaign, member.Card, now.AddDays(-2), ct, now)).LastOrDefault();
            if (latest == null || latest.AtUtc < now.AddHours(-24) || !ResultObservationReader.Known(member.Card, latest)) return false;
            return member.Card.Rule.RuleId == "R02" ? latest.Budget < member.Applied.Budget : !ResultObservationReader.Preserved(member, latest);
        }
        if (rec.OtherChangeAtUtc != null)
        {
            var campaign = await db.Campaigns.AsNoTracking().SingleAsync(x => x.Id == rec.CampaignId, ct);
            var original = history.Exposures.OrderBy(x => x.ShownAtUtc).Select(Read).LastOrDefault(c => c.Presentation?.Primary.Kind == "reduce_bid") ?? history.Card;
            var latest = (await new ResultObservationReader(db).ReadAsync(campaign, original, now.AddDays(-2), ct, now)).LastOrDefault();
            return !card.Preliminary && ResultsService.Actionable(card) && latest is { State: "ready", Excluded: false } && latest.AtUtc >= now.AddHours(-24) &&
                end > WbSyncPlanner.Today(rec.OtherChangeAtUtc.Value);
        }
        return refusalExpired || rec.ArchivedAtUtc != null && !card.Preliminary;
    }

    public async Task<RecommendationResponse> ApplyAsync(RecommendationResponse report, bool autoReplenishment, CancellationToken ct)
    {
        if (report.CampaignId == null) return report;
        var histories = await LoadAsync(report.CampaignId.Value, ct);
        var cards = new List<RecommendationCard>();
        var today = WbSyncPlanner.Today(clock.GetUtcNow().UtcDateTime);
        foreach (var card in report.Cards.DistinctBy(Identity))
        {
            if (autoReplenishment && card.Rule.RuleId == "R02") continue;
            var history = Find(histories, card);
            if (card.Presentation?.Control?.Excluded == true) continue;
            if (history == null)
            {
                if (card.Decision?.Status == "irrelevant" || card.Decision?.Status == "snoozed" && card.Decision.SnoozeUntil > today) continue;
                cards.Add(card.Decision?.Status == "snoozed" ? card with { Decision = null } : card);
                continue;
            }
            if (await NewOccurrenceAsync(history, card, report.Window.EndDate, ct, report.CalculatedAtUtc)) { cards.Add(card with { Decision = null }); continue; }
            if (history.Recommendation.MeasurementId != null || history.Recommendation.OtherChangeAtUtc != null) continue;
            var decision = history.Latest;
            // A preference cleared in Norms must not continue suppressing budget advice.
            if (decision?.ReasonCode == CampaignRecommendationSettings.AutoReplenishmentReason) decision = null;
            if (decision?.Status == "irrelevant" || decision?.Status == "snoozed" && decision.SnoozeUntil > today) continue;
            if (decision?.Status == "snoozed") decision = null;
            var presented = card;
            if (decision is { Status: "accepted" } && card.Presentation != null)
            {
                var accepted = Read(history.Exposures.Single(e => e.RunId == decision.RunId && e.EvaluationId == decision.EvaluationId && e.RuleId == decision.RuleId));
                if (accepted.Presentation != null)
                    presented = card with { Presentation = card.Presentation with { Primary = accepted.Presentation.Primary, Alternatives = accepted.Presentation.Alternatives } };
            }
            cards.Add(presented with { Decision = decision == null ? null : new(decision.Status, decision.SnoozeUntil, decision.Reason, decision.Comment, decision.CreatedAtUtc) });
        }
        var hiddenBudget = autoReplenishment && report.Cards.Any(c => c.Rule.RuleId == "R02");
        return report with { Cards = cards.ToArray(), Packages = RecommendationActions.Packages(cards),
            Coverage = report.Coverage with { Triggered = cards.Count },
            Status = hiddenBudget && cards.Count == 0 ? "budget_auto_replenishment" : report.Status,
            Message = hiddenBudget && cards.Count == 0 ? "Рекомендация о бюджете скрыта: в «Нормах» отмечено автопополнение. Остальные проверки доступны в кампании." :
                cards.Count == 0 && report.Cards.Count > 0 ? "Новых рекомендаций нет. Сохранённые решения и результаты доступны в разделе «Результаты»." : report.Message };
    }

    // Called by the worker only. API refreshes and repeated calculations are not additional nights.
    public async Task ObserveConditionsAsync(RecommendationResponse report, CancellationToken ct)
    {
        if (report.CampaignId == null) return;
        var now = clock.GetUtcNow().UtcDateTime; var today = WbSyncPlanner.Today(now);
        if (report.CalculatedAtUtc == null || WbSyncPlanner.Today(report.CalculatedAtUtc.Value) != today || report.Window.EndDate > today.AddDays(-2)) return;
        var histories = await LoadAsync(report.CampaignId.Value, ct);
        foreach (var history in histories.GroupBy(h => Identity(h.Card)).Select(g => g.OrderByDescending(h => h.Recommendation.FirstShownAtUtc).First())
            .Where(h => h.Recommendation.MeasurementId == null && h.Recommendation.OtherChangeAtUtc == null && h.Recommendation.ArchivedAtUtc == null))
        {
            var matching = report.Evaluations.Where(e => Identity(history.Card with { Subject = e.Subject }) == Identity(history.Card)).ToArray();
            if (matching.Length != 1) continue; // Ambiguous normalized subjects cannot establish disappearance.
            var evaluation = matching[0];
            var rule = evaluation?.Rules.SingleOrDefault(r => r.RuleId == history.Card.Rule.RuleId);
            if (evaluation?.Quality is not { Complete: true, FieldsVerified: true, SourcesComparable: true } ||
                rule is not { Enabled: true } || rule.Status is not ("triggered" or "not_triggered")) continue;
            var rec = await db.WbResultRecommendations.SingleAsync(x => x.Id == history.Recommendation.Id, ct);
            if (rule.Status == "triggered") { rec.ClearConditionNights = 0; rec.LastConditionDate = today; continue; }
            if (rec.LastConditionDate >= today) continue;
            rec.ClearConditionNights = rec.LastConditionDate == today.AddDays(-1) ? rec.ClearConditionNights + 1 : 1;
            rec.LastConditionDate = today;
            if (rec.ClearConditionNights >= 3)
            {
                rec.ArchivedAtUtc = now;
                logger?.LogInformation("Recommendation {RecommendationId} archived after three confirmed daily checks", rec.Id);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

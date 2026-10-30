using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record ResultExposureRequest(Guid RunId, Guid EvaluationId, string RuleId);

public sealed class ResultsService(EcomadsDbContext db, TimeProvider clock, ILogger<ResultsService>? logger = null)
{
    public static readonly JsonSerializerOptions Json = ResultObservationReader.Json;
    public static string Key(RecommendationCard c) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{c.Subject.CampaignId}:{c.Subject.ArticleId}:{c.Subject.ClusterName}:{c.Rule.RuleId}")));
    public static ResultMember[] Members(WbResultMeasurement m) => JsonSerializer.Deserialize<ResultMember[]>(m.MembersJson, Json) ?? [];
    private static RecommendationCard Card(WbResultExposure e) => JsonSerializer.Deserialize<RecommendationCard>(e.CardJson, Json)!;
    internal static bool Actionable(RecommendationCard c) => c.Presentation?.Primary.Kind is
        "exclude" or "reduce_bid" or "reduce_article_bid" or "reduce_campaign_bid" or "pause_campaign" or "remove_article" or "top_up_budget";

    public async Task<bool> RegisterAsync(Guid seller, IReadOnlyList<ResultExposureRequest> requested, CancellationToken ct)
    {
        var runs = await db.WbRecommendationRuns.AsNoTracking().Where(x => requested.Select(r => r.RunId).Contains(x.Id) && x.SellerId == seller &&
            db.Stores.Any(s => s.Id == x.StoreId && s.SellerId == seller)).ToDictionaryAsync(x => x.Id, ct);
        var cards = new List<(WbRecommendationRun Run, RecommendationCard Card)>();
        foreach (var request in requested.Distinct())
        {
            if (!runs.TryGetValue(request.RunId, out var run)) return false;
            var card = JsonSerializer.Deserialize<RecommendationResponse>(run.SnapshotJson, Json)?.Cards
                .SingleOrDefault(x => x.EvaluationId == request.EvaluationId && x.Rule.RuleId == request.RuleId);
            if (card == null || card.Subject.StoreId != run.StoreId || card.Subject.CampaignId != run.CampaignId) return false;
            cards.Add((run, card));
        }
        foreach (var group in cards.GroupBy(x => x.Run.StoreId))
        {
            await LockedAsync(group.Key, async () => {
                var now = clock.GetUtcNow().UtcDateTime;
                var lifecycle = new RecommendationLifecycle(db, clock);
                var histories = new Dictionary<Guid, List<RecommendationLifecycle.History>>();
                foreach (var campaignId in group.Select(x => x.Run.CampaignId).Distinct())
                    histories[campaignId] = (await lifecycle.LoadAsync(campaignId, ct)).ToList();
                foreach (var (run, card) in group)
                {
                    var key = Key(card);
                    var history = RecommendationLifecycle.Find(histories[run.CampaignId], card);
                    // A retry for the same immutable exposure always belongs to its original occurrence.
                    var existingExposure = await db.WbResultExposures.AsNoTracking().FirstOrDefaultAsync(e =>
                        e.RunId == run.Id && e.EvaluationId == card.EvaluationId && e.RuleId == card.Rule.RuleId, ct);
                    var rec = existingExposure != null
                        ? await db.WbResultRecommendations.SingleAsync(x => x.Id == existingExposure.RecommendationId, ct)
                        : history == null || await lifecycle.NewOccurrenceAsync(history, card, run.EndDate, ct, run.CalculatedAtUtc) ? null
                        : await db.WbResultRecommendations.SingleAsync(x => x.Id == history.Recommendation.Id, ct);
                    if (rec == null) { rec = new() { Id = Guid.NewGuid(), SellerId = seller, StoreId = run.StoreId, CampaignId = run.CampaignId,
                        CampaignName = await db.Campaigns.Where(x => x.Id == run.CampaignId).Select(x => x.Name).SingleAsync(ct),
                        SubjectKey = key, FirstShownAtUtc = now, LastShownAtUtc = now }; db.WbResultRecommendations.Add(rec);
                        logger?.LogInformation("Recommendation occurrence {RecommendationId} registered for campaign {CampaignId}, rule {RuleId}", rec.Id, run.CampaignId, card.Rule.RuleId); }
                    rec.LastShownAtUtc = now;
                    if (!await db.WbResultExposures.AnyAsync(x => x.RecommendationId == rec.Id && x.RunId == run.Id && x.EvaluationId == card.EvaluationId && x.RuleId == card.Rule.RuleId, ct) &&
                        !db.WbResultExposures.Local.Any(x => x.RecommendationId == rec.Id && x.RunId == run.Id && x.EvaluationId == card.EvaluationId && x.RuleId == card.Rule.RuleId))
                        db.WbResultExposures.Add(new() { Id = Guid.NewGuid(), RecommendationId = rec.Id, RunId = run.Id, EvaluationId = card.EvaluationId,
                            RuleId = card.Rule.RuleId, ShownAtUtc = now, CardJson = JsonSerializer.Serialize(card, Json) });
                    await db.SaveChangesAsync(ct);
                    var shown = await db.WbResultExposures.AsNoTracking().Where(e => e.RecommendationId == rec.Id).ToArrayAsync(ct);
                    histories[run.CampaignId].RemoveAll(h => h.Recommendation.Id == rec.Id);
                    histories[run.CampaignId].Add(new(rec, shown, history?.Recommendation.Id == rec.Id ? history.Decisions : []));
                }
            }, ct);
        }
        return true;
    }

    public async Task RunStoreAsync(Guid storeId, CancellationToken ct) => await LockedAsync(storeId, async () =>
    {
        var now = clock.GetUtcNow().UtcDateTime; var today = WbSyncPlanner.Today(now);
        await ImportLegacyAsync(storeId, ct);
        var recs = await db.WbResultRecommendations.Where(x => x.StoreId == storeId && !x.Legacy).ToArrayAsync(ct);
        var exposures = await db.WbResultExposures.Where(x => recs.Select(r => r.Id).Contains(x.RecommendationId)).OrderBy(x => x.ShownAtUtc).ToArrayAsync(ct);
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId).ToDictionaryAsync(x => x.Id, ct);
        var measurements = (await db.WbResultMeasurements.Where(x => x.StoreId == storeId).ToArrayAsync(ct)).ToList();
        var runIds = exposures.Select(x => x.RunId).Distinct().ToArray();
        var decisions = await db.WbSnapshotDecisions.AsNoTracking().Where(x => runIds.Contains(x.RunId) && x.Status == "accepted").ToArrayAsync(ct);
        var detectedActions = new List<(WbResultRecommendation Rec, WbResultExposure Exposure, RecommendationCard Card, ResultSetting Before, ResultSetting Applied)>();
        foreach (var rec in recs.Where(x => x.MeasurementId == null && x.OtherChangeAtUtc == null && x.ArchivedAtUtc == null))
        {
            if (!campaigns.TryGetValue(rec.CampaignId, out var campaign)) continue;
            var versions = exposures.Where(x => x.RecommendationId == rec.Id).ToArray();
            if (versions.Length == 0) continue;
            var accepted = decisions.Where(d => versions.Any(e => e.RunId == d.RunId && e.EvaluationId == d.EvaluationId && e.RuleId == d.RuleId))
                .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id).FirstOrDefault();
            if (accepted != null)
            {
                var acceptedVersion = versions.Single(e => e.RunId == accepted.RunId && e.EvaluationId == accepted.EvaluationId && e.RuleId == accepted.RuleId);
                if (Actionable(Card(acceptedVersion))) versions = [acceptedVersion];
            }
            var saved = rec.BaselineJson == null ? null : JsonSerializer.Deserialize<ResultSetting>(rec.BaselineJson, Json);
            var possible = versions.Select(Card).Where(c => c.Presentation != null).SelectMany(c =>
                new[] { c.Presentation!.Primary }.Concat(c.Presentation.Alternatives).Select(a => c with { Presentation = c.Presentation with { Primary = a } }))
                .Where(Actionable).DistinctBy(c => (c.Presentation!.Primary.Kind, c.Presentation.Primary.Level)).ToArray();
            var found = new List<(WbResultExposure Exposure, RecommendationCard Card, ResultSetting Before, ResultSetting Applied)>();
            ResultSetting? latestBaseline = saved;
            foreach (var template in possible)
            {
                var source = ResultObservationReader.Source(template);
                var observations = await new ResultObservationReader(db).ReadAsync(campaign, template, rec.FirstShownAtUtc.AddDays(-2), ct);
                var baseline = saved?.Source == source && ResultObservationReader.Known(template, saved) ? saved :
                    observations.LastOrDefault(x => x.AtUtc <= rec.FirstShownAtUtc && ResultObservationReader.Known(template, x));
                foreach (var current in observations.Where(x => x.AtUtc > rec.FirstShownAtUtc && x.AtUtc <= now))
                {
                    if (baseline != null && current.AtUtc <= baseline.AtUtc) continue;
                    var version = versions.LastOrDefault(x => x.ShownAtUtc < current.AtUtc);
                    if (version == null || !ResultObservationReader.Known(template, current)) continue;
                    if (baseline == null) { baseline = current; continue; }
                    var original = Card(version);
                    var action = original.Presentation == null ? null : new[] { original.Presentation.Primary }.Concat(original.Presentation.Alternatives)
                        .FirstOrDefault(a => a.Kind == template.Presentation!.Primary.Kind && a.Level == template.Presentation.Primary.Level);
                    var card = action == null ? original : original with { Presentation = original.Presentation! with { Primary = action } };
                    if (action != null && version.ShownAtUtc > baseline.AtUtc)
                        baseline = observations.LastOrDefault(x => x.AtUtc <= version.ShownAtUtc && ResultObservationReader.Known(card, x)) ?? baseline;
                    if (action != null && ResultObservationReader.Applied(card, baseline, current)) { found.Add((version, card, baseline, current)); break; }
                    if (action == null || ResultObservationReader.AdvanceBaseline(card, baseline, current)) baseline = current;
                }
                if (baseline != null && ResultObservationReader.Source(Card(versions[^1])) == source) latestBaseline = baseline;
            }
            if (found.Count > 0)
            {
                var first = found.OrderBy(x => x.Applied.AtUtc).ThenBy(x => x.Card.Presentation!.Primary.Kind == Card(x.Exposure).Presentation!.Primary.Kind ? 0 : 1).First();
                detectedActions.Add((rec, first.Exposure, first.Card, first.Before, first.Applied));
            }
            else
            {
                if (latestBaseline != null) rec.BaselineJson = JsonSerializer.Serialize(latestBaseline, Json);
                // Exclusion is a different action from lowering a bid; it does not establish monetary savings.
                foreach (var version in versions.Where(e => Card(e).Presentation?.Primary.Kind == "reduce_bid"))
                {
                    var card = Card(version);
                    var states = await new ResultObservationReader(db).ReadAsync(campaign, card, rec.FirstShownAtUtc.AddDays(-2), ct, now);
                    var before = states.LastOrDefault(s => s.AtUtc <= version.ShownAtUtc && s.State == "ready" && s.Excluded.HasValue);
                    var changed = states.FirstOrDefault(s => s.AtUtc > version.ShownAtUtc && s.State == "ready" && s.Excluded == true);
                    if (before?.Excluded == false && changed != null)
                    {
                        rec.OtherChangeAtUtc = changed.AtUtc;
                        logger?.LogInformation("Recommendation {RecommendationId}: exclusion detected instead of bid reduction at {AtUtc}", rec.Id, changed.AtUtc);
                        break;
                    }
                }
            }
        }
        // Process by observation time, independent of row/UUID order in PostgreSQL.
        foreach (var found in detectedActions.OrderBy(x => x.Applied.AtUtc).ThenBy(x => x.Rec.Id))
        {
                var (rec, version, card, baseline, current) = found;
                var campaign = campaigns[rec.CampaignId];
                var detected = WbSyncPlanner.Today(current.AtUtc);
                var ongoing = measurements.Where(m => m.CampaignId == campaign.Id && m.DetectedDate <= detected &&
                    m.After.End >= detected && m.InterruptedAtUtc == null).OrderByDescending(m => m.DetectedDate).FirstOrDefault();
                if (ongoing != null && detected > ongoing.DetectedDate.AddDays(3))
                { ongoing.InterruptedAtUtc = current.AtUtc; ongoing.Status = "not_evaluated"; ongoing.Reason = "Прерван другим изменением"; ongoing.Savings = null; ongoing.ConfirmedAtUtc = null; ongoing = null; }
                if (ongoing == null)
                {
                    var previousDate = WbSyncPlanner.Today(baseline.AtUtc);
                    ongoing = new() { Id = Guid.NewGuid(), SellerId = rec.SellerId, StoreId = storeId, CampaignId = campaign.Id, DetectedDate = detected,
                        Before = new() { Start = previousDate.AddDays(-7), End = previousDate.AddDays(-1) },
                        After = new() { Start = detected.AddDays(1), End = detected.AddDays(7) } };
                    db.WbResultMeasurements.Add(ongoing); measurements.Add(ongoing);
                }
                var member = new ResultMember(rec.Id, version.Id, card, baseline, current);
                ongoing.MembersJson = JsonSerializer.Serialize(Members(ongoing).Append(member).ToArray(), Json);
                rec.MeasurementId = ongoing.Id;
                logger?.LogInformation("Recommendation {RecommendationId} applied: action {Action}, measurement {MeasurementId}, observation {ObservationId}", rec.Id, card.Presentation!.Primary.Kind, ongoing.Id, current.Id);
        }
        await db.SaveChangesAsync(ct);
        foreach (var m in measurements)
        {
            if (!campaigns.TryGetValue(m.CampaignId, out var campaign)) continue;
            var members = Members(m);
            var latest = new List<ResultSetting>(); var preserved = members.Length > 0;
            foreach (var member in members)
            {
                var historyEnd = TimeZoneInfo.ConvertTimeToUtc(m.DetectedDate.AddDays(30).ToDateTime(TimeOnly.MinValue), WbRefreshClock.Moscow);
                var reader = new ResultObservationReader(db);
                var states = await reader.ReadAsync(campaign, member.Card, member.Applied.AtUtc, ct, historyEnd < now ? historyEnd : now);
                var current = today < m.DetectedDate.AddDays(30) ? (await reader.ReadAsync(campaign, member.Card, now.AddDays(-2), ct, now)).LastOrDefault() : null;
                if (current == null || current.AtUtc < now.AddHours(-24) || !ResultObservationReader.Preserved(member, current)) preserved = false;
                // Cancellation permanently ends the original projection; a later application belongs to a new occurrence.
                if (states.Any(s => s.State == "ready" && !ResultObservationReader.Preserved(member, s))) preserved = false;
                if (current != null) latest.Add(current);
                // Reversion after the grouping interval is another change, even without a new recommendation.
                var reversal = states.FirstOrDefault(s => WbSyncPlanner.Today(s.AtUtc) > m.DetectedDate.AddDays(3) && s.AtUtc <=
                    TimeZoneInfo.ConvertTimeToUtc(m.After.End.AddDays(1).ToDateTime(TimeOnly.MinValue), WbRefreshClock.Moscow) &&
                    s.State == "ready" && !ResultObservationReader.Preserved(member, s));
                if (reversal != null) m.InterruptedAtUtc ??= reversal.AtUtc;
            }
            m.ActionPreserved = preserved; m.ContinuationCheckedAtUtc = latest.Count == members.Length && latest.Count > 0 ? latest.Min(x => x.AtUtc) : null;
            if (members.Length > 0 && members.All(x => x.Card.Rule.RuleId is not ("R01" or "R02")))
            {
                var metadataCard = members[0].Card with { Presentation = members[0].Card.Presentation! with { Primary = new("pause_campaign", "", Level: "campaign") } };
                var status = (await new ResultObservationReader(db).ReadAsync(campaign, metadataCard, now.AddDays(-2), ct)).LastOrDefault(x => x.AtUtc <= now);
                if (status?.State != "ready" || status.Status != 9 || status.AtUtc < now.AddHours(-24)) m.ActionPreserved = false;
                if (status != null && m.ContinuationCheckedAtUtc > status.AtUtc) m.ContinuationCheckedAtUtc = status.AtUtc;
            }
            m.ContinuationReason = m.ActionPreserved ? null : "Сохранение действия и активность кампании не подтверждены свежими снимками";
            var unsupported = members.Select(x => (x.Card.Rule.RuleId, x.Card.Presentation!.Primary.Kind)).Distinct().Count() > 1 ||
                members.Any(x => x.Card.Rule.RuleId is "R01" or "R03" && x.Card.Presentation!.Primary.Kind.StartsWith("reduce_"));
            if (today < m.DetectedDate.AddDays(10) && !unsupported && m.InterruptedAtUtc == null && members.All(x => x.Card.Rule.RuleId != "R02")) continue;
            var before = await new ResultMetricsReader(db).ReadAsync(campaign, members, m.Before, ct);
            var after = await new ResultMetricsReader(db).ReadAsync(campaign, members, m.After, ct);
            var restrictions = await new ResultSettingsAudit(db).CheckAsync(campaign, members, m, ct);
            if (m.InterruptedAtUtc != null) restrictions.Add("Прерван другим изменением");
            var outcome = ResultCalculation.Calculate(members, before, after, restrictions);
            if (m.InterruptedAtUtc != null) outcome = new("not_evaluated", "Прерван другим изменением", null, "Замер остановлен");
            var evidence = new ResultEvidence(ResultCalculation.Version, before, after, outcome, restrictions);
            var json = JsonSerializer.Serialize(evidence, Json);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var previous = await db.WbResultRevisions.Where(x => x.MeasurementId == m.Id).OrderByDescending(x => x.Number).FirstOrDefaultAsync(ct);
            if (previous?.Fingerprint != hash)
            {
                m.Revision++; db.WbResultRevisions.Add(new() { Id = Guid.NewGuid(), MeasurementId = m.Id, Number = m.Revision,
                    Fingerprint = hash, CalculatedAtUtc = now, EvidenceJson = json });
                m.Status = outcome.Status; m.Reason = outcome.Reason; m.Savings = outcome.Savings;
                m.ConfirmedAtUtc = outcome.Status == "confirmed" ? m.ConfirmedAtUtc ?? now : null;
            }
        }
        await db.SaveChangesAsync(ct);
    }, ct);

    private async Task LockedAsync(Guid store, Func<Task> action, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"results:" + store}, 0))", ct);
            await action(); await tx.CommitAsync(ct);
        });
    }

    public Task ObserveConditionsAsync(Guid store, RecommendationResponse report, CancellationToken ct) =>
        LockedAsync(store, () => new RecommendationLifecycle(db, clock, logger).ObserveConditionsAsync(report, ct), ct);

    private async Task ImportLegacyAsync(Guid store, CancellationToken ct)
    {
        var decisions = await (from d in db.WbSnapshotDecisions join r in db.WbRecommendationRuns on d.RunId equals r.Id
            where r.StoreId == store && !db.WbResultExposures.Any(x => x.RunId == r.Id && x.EvaluationId == d.EvaluationId && x.RuleId == d.RuleId)
            select new { Decision = d, Run = r }).ToArrayAsync(ct);
        foreach (var item in decisions.DistinctBy(x => (x.Decision.RunId, x.Decision.EvaluationId, x.Decision.RuleId)))
        {
            var card = JsonSerializer.Deserialize<RecommendationResponse>(item.Run.SnapshotJson, Json)?.Cards
                .SingleOrDefault(c => c.EvaluationId == item.Decision.EvaluationId && c.Rule.RuleId == item.Decision.RuleId);
            if (card == null) continue;
            var rec = new WbResultRecommendation { Id = Guid.NewGuid(), SellerId = item.Run.SellerId, StoreId = store, CampaignId = item.Run.CampaignId,
                SubjectKey = Key(card), FirstShownAtUtc = item.Decision.CreatedAtUtc, LastShownAtUtc = item.Decision.CreatedAtUtc, Legacy = true,
                CampaignName = await db.Campaigns.Where(x => x.Id == item.Run.CampaignId).Select(x => x.Name).SingleAsync(ct) };
            db.WbResultRecommendations.Add(rec);
            db.WbResultExposures.Add(new() { Id = Guid.NewGuid(), RecommendationId = rec.Id, RunId = item.Run.Id, EvaluationId = card.EvaluationId,
                RuleId = card.Rule.RuleId, ShownAtUtc = item.Decision.CreatedAtUtc, CardJson = JsonSerializer.Serialize(card, Json) });
        }
        await db.SaveChangesAsync(ct);
    }
}

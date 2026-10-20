using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed record ResultJournalMember(Guid RecommendationId, string RuleId, string Object, string Action,
    string? Decision, string? Reason, DateOnly? SnoozeUntil, DateTime FirstShownAtUtc, bool Legacy);
public sealed record ResultJournalRow(Guid Id, string Campaign, string Status, string Reason, decimal? Savings,
    decimal? Forecast, DateOnly? AppliedDate, DateOnly? ExpectedDate, int Revision, IReadOnlyList<ResultJournalMember> Members);
public sealed record ResultFunnel(int Issued, int Accepted, int Applied, int Confirmed, int AppliedWithoutAcceptance,
    int Snoozed, int Irrelevant, int Undecided, IReadOnlyDictionary<string, int> RejectionReasons);
public sealed record ResultWeek(DateOnly Start, decimal Savings, decimal Cumulative);
public sealed record ResultSummary(decimal Savings, int ConfirmedMeasurements, int ConfirmedRecommendations, decimal Forecast,
    int Measuring, int? OrdersBefore, int? OrdersAfter, string? OrdersReason);
public sealed record ResultsReport(bool Enabled, DateTime? CollectionStartedAtUtc, DateOnly? StartDate, DateOnly EndDate,
    ResultSummary Summary, ResultFunnel Funnel, IReadOnlyList<ResultWeek> Weeks, IReadOnlyList<ResultJournalRow> Rows,
    int Total, int Page, int PageSize);
public sealed record ResultDetail(ResultJournalRow Row, RecommendationPeriod? BeforeWindow, RecommendationPeriod? AfterWindow,
    IReadOnlyList<ResultMember> Applications, IReadOnlyList<ResultDetailRevision> Revisions);
public sealed record ResultDetailRevision(int Number, DateTime CalculatedAtUtc, ResultEvidence Evidence);

public sealed class ResultsReportReader(EcomadsDbContext db, TimeProvider clock)
{
    public async Task<ResultsReport> ReadAsync(Guid seller, Guid? store, DateOnly? start, DateOnly end, string filter,
        int page, int pageSize, bool enabled, CancellationToken ct, Guid? rowId = null)
    {
        var now = clock.GetUtcNow().UtcDateTime; var today = WbSyncPlanner.Today(now);
        var stores = await db.Stores.AsNoTracking().Where(x => x.SellerId == seller && (store == null || x.Id == store)).Select(x => x.Id).ToArrayAsync(ct);
        var recommendations = await db.WbResultRecommendations.AsNoTracking().Where(x => x.SellerId == seller && stores.Contains(x.StoreId)).ToArrayAsync(ct);
        var recIds = recommendations.Select(x => x.Id).ToArray();
        var exposures = await db.WbResultExposures.AsNoTracking().Where(x => recIds.Contains(x.RecommendationId)).ToArrayAsync(ct);
        var runIds = exposures.Select(x => x.RunId).ToArray();
        var decisions = await db.WbSnapshotDecisions.AsNoTracking().Where(x => runIds.Contains(x.RunId)).ToArrayAsync(ct);
        var measurements = await db.WbResultMeasurements.AsNoTracking().Where(x => x.SellerId == seller && stores.Contains(x.StoreId)).ToArrayAsync(ct);
        var measurementIds = measurements.Select(x => x.Id).ToArray();
        var revisions = await db.WbResultRevisions.AsNoTracking().Where(x => measurementIds.Contains(x.MeasurementId)).ToArrayAsync(ct);
        bool Period(DateTime at) { var date = WbSyncPlanner.Today(at); return (start == null || date >= start) && date <= end; }
        WbSnapshotDecision? Decision(WbResultRecommendation rec)
        {
            var shown = exposures.Where(x => x.RecommendationId == rec.Id).ToArray();
            return decisions.Where(d => shown.Any(x => x.RunId == d.RunId && x.EvaluationId == d.EvaluationId && x.RuleId == d.RuleId))
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
        }
        var chosen = recommendations.Where(x => !x.Legacy && Period(x.FirstShownAtUtc)).ToArray();
        var confirmed = measurements.Where(x => x.Status == "confirmed" && x.ConfirmedAtUtc.HasValue && Period(x.ConfirmedAtUtc.Value)).ToArray();
        var funnel = new ResultFunnel(chosen.Length, chosen.Count(x => Decision(x)?.Status == "accepted"), chosen.Count(x => x.MeasurementId != null),
            chosen.Count(x => measurements.Any(m => m.Id == x.MeasurementId && m.Status == "confirmed")),
            chosen.Count(x => x.MeasurementId != null && Decision(x)?.Status != "accepted"), chosen.Count(x => Decision(x)?.Status == "snoozed"),
            chosen.Count(x => Decision(x)?.Status == "irrelevant"), chosen.Count(x => Decision(x) == null),
            chosen.Where(x => Decision(x)?.Status == "irrelevant").GroupBy(x => Decision(x)!.Reason ?? "Без причины").ToDictionary(g => g.Key, g => g.Count()));
        var allRows = new List<ResultJournalRow>();
        foreach (var group in recommendations.GroupBy(x => x.MeasurementId ?? x.Id))
        {
            if (!group.Any(x => Period(x.FirstShownAtUtc))) continue;
            var m = measurements.SingleOrDefault(x => x.Id == group.Key);
            var members = new List<ResultJournalMember>();
            var status = m?.Status; var reason = m?.Reason;
            foreach (var rec in group)
            {
                var exposure = exposures.Where(x => x.RecommendationId == rec.Id).OrderByDescending(x => x.ShownAtUtc).FirstOrDefault();
                if (exposure == null) continue;
                var card = m == null ? JsonSerializer.Deserialize<RecommendationCard>(exposure.CardJson, ResultsService.Json)! :
                    ResultsService.Members(m).Single(x => x.RecommendationId == rec.Id).Card;
                var d = Decision(rec);
                members.Add(new(rec.Id, card.Rule.RuleId, card.Subject.ClusterName ?? card.Subject.NomenclatureName ?? "Вся кампания",
                    card.Presentation?.Primary.Label ?? card.Rule.Explanation, d?.Status, d?.Reason, d?.SnoozeUntil, rec.FirstShownAtUtc, rec.Legacy));
                if (m != null) continue;
                status = rec.Legacy ? "historical" : d?.Status is "snoozed" or "irrelevant" ? d.Status : "pending";
                reason = rec.Legacy ? "История решения: исходные снимки применения отсутствуют" : "Применение ещё не подтверждено";
                if (!rec.Legacy && d?.Status == "accepted" && now >= d.CreatedAtUtc.AddDays(3))
                {
                    var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rec.CampaignId && stores.Contains(x.StoreId), ct);
                    var current = campaign == null ? null : (await new ResultObservationReader(db).ReadAsync(campaign, card, now.AddDays(-2), ct)).LastOrDefault(x => x.AtUtc <= now);
                    var baseline = rec.BaselineJson == null ? null : JsonSerializer.Deserialize<ResultSetting>(rec.BaselineJson, ResultsService.Json);
                    if (current?.State == "ready" && current.AtUtc >= now.AddHours(-24) && baseline != null &&
                        !ResultObservationReader.Applied(card, baseline, current)) { status = "not_applied"; reason = "Принято больше трёх дней назад; изменения пока не обнаружены"; }
                    else reason = "Нет свежего подтверждения применения";
                }
            }
            if (members.Count == 0) continue;
            allRows.Add(new(group.Key, group.First().CampaignName, status ?? "pending", reason ?? "", m?.Savings,
                m == null ? null : Forecast(m, now), m?.DetectedDate, m == null ? null : m.DetectedDate.AddDays(10), m?.Revision ?? 0,
                members.OrderBy(x => x.FirstShownAtUtc).ThenBy(x => x.RecommendationId).ToArray()));
        }
        // Before snapshot decisions, the UI persisted only a key, period and last decision.
        // Read those records directly: inventing an exposure or mapping them to today's rules would attribute false history.
        var legacyCampaigns = await db.Campaigns.AsNoTracking().Where(x => stores.Contains(x.StoreId)).ToDictionaryAsync(x => x.Id, ct);
        var legacyDecisions = await db.WbRecommendationDecisions.AsNoTracking().Where(x => x.SellerId == seller).ToArrayAsync(ct);
        foreach (var decision in legacyDecisions.Where(x => Period(x.UpdatedAtUtc)))
        {
            var parts = decision.RecommendationKey.Split(':');
            if (parts.Length < 2 || !Guid.TryParse(parts[1], out var campaignId) || !legacyCampaigns.TryGetValue(campaignId, out var campaign)) continue;
            var label = parts[0] switch { "ctr" when parts.Length == 2 => "Рекомендация по CTR", "drr" when parts.Length == 2 => "Рекомендация по ДРР",
                "cluster" when parts.Length == 4 => "Рекомендация по кластеру", _ => null };
            if (label == null) continue;
            var subject = parts[0] == "cluster" ? $"Товар {parts[2]} · кластер из прежней версии" : "Вся кампания";
            var reason = $"Сохранённая отметка прежней версии за {decision.StartDate:dd.MM.yyyy} — {decision.EndDate:dd.MM.yyyy}. Исходный текст и снимки применения не сохранялись; денежный эффект не оценивается.";
            allRows.Add(new(decision.Id, campaign.Name, "historical", reason, null, null, null, null, 0,
                [new(decision.Id, "История", subject, label, decision.Status, null, null, decision.UpdatedAtUtc, true)]));
        }
        var filtered = allRows.Where(x => rowId == null || x.Id == rowId).Where(x => filter switch {
            "result" => x.Status is "confirmed" or "not_confirmed" or "not_evaluated" or "redistributed" or "done" or "no_effect",
            "measuring" => x.Status == "measuring", "not_applied" => x.Status == "not_applied",
            "rejected" => x.Members.Any(m => m.Decision is "irrelevant" or "snoozed"), _ => true }).OrderByDescending(x => x.Members.Min(m => m.FirstShownAtUtc)).ThenBy(x => x.Id).ToArray();
        page = Math.Min(page, Math.Max(0, (filtered.Length - 1) / pageSize));
        var weeks = confirmed.GroupBy(x => {
            var date = WbSyncPlanner.Today(x.ConfirmedAtUtc!.Value); return date.AddDays(-((int)date.DayOfWeek + 6) % 7); })
            .OrderBy(x => x.Key).ToArray();
        decimal cumulative = 0;
        var points = weeks.Select(g => { var sum = g.Sum(x => x.Savings ?? 0); cumulative += sum; return new ResultWeek(g.Key, sum, cumulative); }).ToArray();
        var objects = new List<(string Key, DateOnly Start, DateOnly End, int Before, int After)>();
        foreach (var m in confirmed)
        {
            var rev = revisions.Where(x => x.MeasurementId == m.Id).OrderByDescending(x => x.Number).FirstOrDefault();
            if (rev == null) continue;
            var evidence = JsonSerializer.Deserialize<ResultEvidence>(rev.EvidenceJson, ResultsService.Json)!;
            foreach (var b in evidence.Before.Objects)
            {
                var a = evidence.After.Objects.SingleOrDefault(x => x.Key == b.Key);
                if (a != null) objects.Add((m.CampaignId + ":" + b.Key, m.Before.Start, m.After.End, b.Units, a.Units));
            }
        }
        var unique = objects.DistinctBy(x => (x.Key, x.Start, x.End)).ToArray();
        var overlapping = unique.Any(x => unique.Any(y => x.Key == y.Key && x.Start != y.Start && x.Start <= y.End && y.Start <= x.End));
        var summary = new ResultSummary(confirmed.Sum(x => x.Savings ?? 0), confirmed.Length, confirmed.Sum(x => ResultsService.Members(x).Length),
            confirmed.Sum(x => Forecast(x, now) ?? 0), measurements.Count(x => x.Status == "measuring" && (start == null || x.DetectedDate >= start) && x.DetectedDate <= end),
            unique.Length == 0 || overlapping ? null : unique.Sum(x => x.Before), unique.Length == 0 || overlapping ? null : unique.Sum(x => x.After),
            overlapping ? "Есть пересекающиеся окна: общий итог заказов не суммируется" : unique.Length == 0 ? "Нет завершённых сопоставимых замеров" : null);
        var started = await db.WbResultObservations.Where(x => db.Campaigns.Any(c => c.Id == x.CampaignId && stores.Contains(c.StoreId)))
            .MinAsync(x => (DateTime?)x.AttemptedAtUtc, ct);
        return new(enabled, started, start, end, summary, funnel, points, filtered.Skip(page * pageSize).Take(pageSize).ToArray(), filtered.Length, page, pageSize);
    }

    public static decimal? Forecast(WbResultMeasurement m, DateTime now)
    {
        if (m.Status != "confirmed" || m.Savings is null or <= 0 || !m.ActionPreserved || m.ContinuationCheckedAtUtc < now.AddHours(-24) ||
            m.ContinuationCheckedAtUtc == null || ResultsService.Members(m).Any(x => x.Card.Rule.RuleId == "R01")) return null;
        return decimal.Round(m.Savings.Value / 7m * Math.Max(0, 30 - (WbSyncPlanner.Today(now).DayNumber - m.DetectedDate.DayNumber)), 2);
    }

    public async Task<ResultDetail?> DetailAsync(Guid seller, Guid id, CancellationToken ct)
    {
        var m = await db.WbResultMeasurements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.SellerId == seller && db.Stores.Any(s => s.Id == x.StoreId && s.SellerId == seller), ct);
        var rec = m == null ? await db.WbResultRecommendations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.SellerId == seller && db.Stores.Any(s => s.Id == x.StoreId && s.SellerId == seller), ct) : null;
        if (m == null && rec == null && !await db.WbRecommendationDecisions.AnyAsync(x => x.Id == id && x.SellerId == seller, ct)) return null;
        var report = await ReadAsync(seller, m?.StoreId ?? rec?.StoreId, null, WbSyncPlanner.Today(clock.GetUtcNow().UtcDateTime), "all", 0, 100, true, ct, id);
        ResultJournalRow? row = report.Rows.SingleOrDefault(x => x.Id == id);
        if (row == null) return null;
        var revisions = m == null ? [] : await db.WbResultRevisions.AsNoTracking().Where(x => x.MeasurementId == id).OrderByDescending(x => x.Number).ToArrayAsync(ct);
        return new(row, m?.Before, m?.After, m == null ? [] : ResultsService.Members(m), revisions.Select(x => new ResultDetailRevision(x.Number, x.CalculatedAtUtc,
            JsonSerializer.Deserialize<ResultEvidence>(x.EvidenceJson, ResultsService.Json)!)).ToArray());
    }
}

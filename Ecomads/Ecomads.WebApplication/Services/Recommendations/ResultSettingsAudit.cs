using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed class ResultSettingsAudit(EcomadsDbContext db)
{
    private sealed record State(DateTime At, string Status, Dictionary<string, string> Values);
    public async Task<List<string>> CheckAsync(Campaign campaign, ResultMember[] members, WbResultMeasurement measurement, CancellationToken ct)
    {
        var reasons = new HashSet<string>();
        var from = TimeZoneInfo.ConvertTimeToUtc(measurement.Before.Start.ToDateTime(TimeOnly.MinValue), WbRefreshClock.Moscow);
        var end = TimeZoneInfo.ConvertTimeToUtc(measurement.After.End.AddDays(1).ToDateTime(TimeOnly.MinValue), WbRefreshClock.Moscow);
        foreach (var source in new[] { "settings", "advertising", "budgets" })
        {
            var states = new List<State>();
            if (source == "settings")
            {
                var rows = await db.WbResultObservations.AsNoTracking().Where(x => x.CampaignId == campaign.Id && x.AttemptedAtUtc >= from.AddDays(-1) && x.AttemptedAtUtc < end).ToArrayAsync(ct);
                foreach (var row in rows) states.Add(Parse(row.AttemptedAtUtc, row.State, row.PayloadJson, source, campaign.WbCampaignId));
            }
            else
            {
                var rows = await db.WbOperationalSnapshots.AsNoTracking().Where(x => x.StoreId == campaign.StoreId && x.Source == source &&
                    (source != "budgets" || x.EntityKey == campaign.WbCampaignId) && x.RequestedAtUtc >= from.AddDays(-1) && x.RequestedAtUtc < end && x.State != "collecting").ToArrayAsync(ct);
                foreach (var row in rows) states.Add(Parse(row.RequestedAtUtc, row.State, row.PayloadJson, source, campaign.WbCampaignId));
            }
            states = states.OrderBy(x => x.At).ToList();
            for (var day = measurement.Before.Start; day <= measurement.After.End; day = day.AddDays(1))
            {
                // The interval between the baseline observation and detection is deliberately excluded.
                if (day > measurement.Before.End && day < measurement.After.Start) continue;
                var midnight = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), WbRefreshClock.Moscow);
                var current = states.LastOrDefault(x => x.At < midnight.AddDays(1) && x.At >= midnight.AddDays(-1));
                if (current == null || current.Status != "ready") reasons.Add("Неполная история настроек: " + source);
            }
            foreach (var pair in states.Zip(states.Skip(1)))
            {
                if (pair.First.Status != "ready" || pair.Second.Status != "ready") continue;
                foreach (var key in pair.First.Values.Keys.Union(pair.Second.Values.Keys))
                {
                    var before = pair.First.Values.GetValueOrDefault(key); var after = pair.Second.Values.GetValueOrDefault(key);
                    if (before == after) continue;
                    // Remaining campaign budget normally decreases with ad spend. Only a rise is an observed top-up.
                    if (source == "budgets" && decimal.TryParse(before, System.Globalization.CultureInfo.InvariantCulture, out var b) &&
                        decimal.TryParse(after, System.Globalization.CultureInfo.InvariantCulture, out var a) && a <= b) continue;
                    var allowed = members.Any(m => WbSyncPlanner.Today(pair.Second.At) >= measurement.DetectedDate &&
                        WbSyncPlanner.Today(pair.Second.At) <= measurement.DetectedDate.AddDays(3) && AllowedKey(m, source, key, after));
                    if (!allowed) reasons.Add("Постороннее изменение настроек кампании: " + source);
                }
            }
        }
        return reasons.Order().ToList();
    }
    private static bool AllowedKey(ResultMember m, string source, string key, string? after)
    {
        var action = m.Card.Presentation!.Primary.Kind; var nm = m.Card.Subject.NomenclatureId;
        return source switch {
            "settings" => action == "exclude" ? key == "minus:" + nm + ":" + m.Card.Subject.ClusterName ||
                after == null && key == "bid:" + nm + ":" + m.Card.Subject.ClusterName :
                action == "reduce_bid" && key == "bid:" + nm + ":" + m.Card.Subject.ClusterName,
            "budgets" => action == "top_up_budget",
            _ => action == "pause_campaign" && key == "status" || action == "remove_article" && (key == "member:" + nm ||
                after == null && key.StartsWith("bid:" + nm + ":", StringComparison.Ordinal)) ||
                action is "reduce_article_bid" or "reduce_campaign_bid" && key.StartsWith("bid:" + nm + ":", StringComparison.Ordinal)
        };
    }
    private static State Parse(DateTime at, string state, string json, string source, string wbId)
    {
        var values = new Dictionary<string, string>();
        try
        {
            string Number(decimal v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (source == "settings")
            {
                var data = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(json, ResultsService.Json);
                if (data?.Bids == null || data.Minus == null) return new(at, "invalid_source", values);
                foreach (var b in data.Bids) values.Add("bid:" + b.NomenclatureId + ":" + b.Cluster, b.BidRubles?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown");
                foreach (var minus in data.Minus) foreach (var cluster in minus.Clusters) values.Add("minus:" + minus.NomenclatureId + ":" + cluster, "true");
            }
            else if (source == "budgets")
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json, ResultsService.Json);
                if (data == null || !data.TryGetValue(wbId, out var budget)) return new(at, "invalid_source", values);
                values.Add("budget", Number(budget));
            }
            else
            {
                var data = JsonSerializer.Deserialize<WbCampaignInfo[]>(json, ResultsService.Json)?.SingleOrDefault(x => x.Id.ToString() == wbId);
                if (data == null) return new(at, "invalid_source", values);
                if (data.NomenclatureIds.Count > 0 && data.NomenclatureIds.Any(nm => data.ArticleBids == null || !data.ArticleBids.Any(b => b.NmId == nm && (b.SearchRubles > 0 || b.RecommendationRubles > 0))))
                    state = "bids_unknown";
                values.Add("status", data.Status.ToString());
                foreach (var nm in data.NomenclatureIds) values.Add("member:" + nm, "true");
                foreach (var bid in data.ArticleBids ?? [])
                {
                    values.Add("bid:" + bid.NmId + ":search", bid.SearchRubles?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown");
                    values.Add("bid:" + bid.NmId + ":recommendations", bid.RecommendationRubles?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown");
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException) { state = "invalid_source"; }
        return new(at, state, values);
    }
}

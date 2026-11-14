using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed class AdvertisingSettingsSync(EcomadsDbContext db, IWbPromotionClient client, IWbTokenService tokens,
    ILogger<AdvertisingSettingsSync> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task RefreshAsync(Store store, DateTime now, CancellationToken ct)
    {
        // Reuse imported metadata. Archived and CPC campaigns cannot use cluster CPM settings.
        var campaigns = await db.Campaigns.Where(x => x.StoreId == store.Id && (x.WbStatus == 9 || x.WbStatus == 11)).ToListAsync(ct);
        var ids = campaigns.Select(x => x.Id).ToArray();
        var rows = await db.WbAdvertisingSettings.Where(x => ids.Contains(x.CampaignId)).ToDictionaryAsync(x => x.CampaignId, ct);
        var candidates = new List<(WbAdvertisingSettings Row, AdvertisingSettingsPayload Payload)>();
        foreach (var campaign in campaigns)
        {
            if (!rows.TryGetValue(campaign.Id, out var row)) continue;
            try
            {
                var payload = JsonSerializer.Deserialize<AdvertisingSettingsPayload>(row.PayloadJson, Json);
                if (payload?.Campaign is not { } advert || advert.Id.ToString() != campaign.WbCampaignId ||
                    payload.CampaignReceivedAtUtc < now.AddHours(-24) || payload.CampaignReceivedAtUtc == null) continue;
                if (advert.Status is not (9 or 11) || advert.PaymentType is not ("cpm" or "cpc") ||
                    advert.BidType is not ("manual" or "unified")) continue;
                if (advert.NomenclatureIds.Count is > 0 and <= 100) candidates.Add((row, payload));
            }
            catch (JsonException) { /* One malformed metadata row must not break other campaigns. */ }
        }
        await RefreshProductBidsAsync(store, candidates, now, ct);
        // Rotate whole campaigns, oldest attempt first. One batch per 12-minute basic-token window.
        var selected = new List<(WbAdvertisingSettings Row, AdvertisingSettingsPayload Payload)>();
        var pairs = new List<WbNormQueryPair>();
        foreach (var candidate in candidates.Where(x => x.Payload.Campaign!.PaymentType == "cpm")
            .OrderBy(x => x.Row.AttemptedAtUtc).ThenBy(x => x.Row.CampaignId))
        {
            var advert = candidate.Payload.Campaign!;
            var campaignPairs = advert.NomenclatureIds.Distinct().Select(n => new WbNormQueryPair(advert.Id, n)).ToArray();
            if (pairs.Count + campaignPairs.Length > 100) continue;
            selected.Add(candidate); pairs.AddRange(campaignPairs);
        }
        if (selected.Count == 0 || !await WbRateLimits.TryReserveAsync(db, store.Id, "cluster_settings", 0, now, ct)) return;
        foreach (var (row, _) in selected) { row.AttemptedAtUtc = now; row.Status = "unknown"; }
        try
        {
            var token = tokens.Unprotect(store.ApiKey!);
            tokens.ReadClaims(token);
            // Keep the original read-only responses in the existing journal. No credentials
            // are captured: WbCaptureHandler stores only paths, pair bodies, and response bodies.
            using var capture = new WbIngestionContext(new WbSyncJob {
                StoreId = store.Id, Kind = "cluster_settings", PairIdsJson = JsonSerializer.Serialize(pairs, Json) }, now);
            using var bids = await client.GetClusterSettingsAsync(token, pairs, false, ct);
            var allBids = new List<(long Advert, AdvertisingCluster Value)>();
            foreach (var bid in bids.RootElement.GetProperty("bids").EnumerateArray())
            {
                var advert = bid.GetProperty("advert_id").GetInt64();
                var nm = bid.GetProperty("nm_id").GetInt64();
                if (!pairs.Contains(new(advert, nm))) throw new JsonException("WB вернул незапрошенную пару.");
                // bid is in base currency units; bid_kopecks is in hundredths of that currency.
                if (bid.TryGetProperty("currency", out var currency) && currency.GetString() != "RUB")
                    throw new JsonException("Валюта ставки WB не RUB.");
                var amount = bid.TryGetProperty("bid_kopecks", out var kopecks) ? kopecks.GetDecimal() / 100m : bid.GetProperty("bid").GetDecimal();
                var cluster = bid.GetProperty("norm_query").GetString();
                if (amount < 0 || string.IsNullOrWhiteSpace(cluster)) throw new JsonException("Некорректная ставка WB.");
                if (allBids.Any(x => x.Advert == advert && x.Value.NomenclatureId == nm.ToString() &&
                    string.Equals(x.Value.Cluster, cluster, StringComparison.OrdinalIgnoreCase))) throw new JsonException("Повторная ставка WB.");
                allBids.Add((advert, new(nm.ToString(), cluster, amount)));
            }
            using var minus = await client.GetClusterSettingsAsync(token, pairs, true, ct);
            var allMinus = new List<(long Advert, AdvertisingMinus Value)>();
            foreach (var item in minus.RootElement.GetProperty("items").EnumerateArray())
            {
                var advert = item.GetProperty("advert_id").GetInt64();
                var nm = item.GetProperty("nm_id").GetInt64();
                if (!pairs.Contains(new(advert, nm))) throw new JsonException("WB вернул незапрошенную пару.");
                allMinus.Add((advert, new(nm.ToString(), item.GetProperty("norm_queries").EnumerateArray()
                    .Select(x => x.GetString() ?? throw new JsonException()).ToArray())));
            }
            // This is a configuration list, not a per-pair coverage report. WB returns only
            // configured minus phrases, rather than one entry per requested member pair.
            // After a validated HTTP 200, an absent requested member has no configured minus.
            foreach (var pair in pairs)
                if (!allMinus.Any(x => x.Advert == pair.AdvertId && x.Value.NomenclatureId == pair.NmId.ToString()))
                    allMinus.Add((pair.AdvertId, new(pair.NmId.ToString(), [])));
            foreach (var (row, payload) in selected)
            {
                var advert = payload.Campaign!;
                var exclusions = allMinus.Where(x => x.Advert == advert.Id).Select(x => x.Value).ToArray();
                // Duplicate entries are invalid. The sparse success normalization above is
                // never applied to failed/malformed responses or unrequested product pairs.
                if (exclusions.GroupBy(x => x.NomenclatureId).Any(g => g.Count() > 1)) continue;
                row.PayloadJson = JsonSerializer.Serialize(payload with {
                    Bids = allBids.Where(x => x.Advert == advert.Id).Select(x => x.Value).ToArray(), Minus = exclusions }, Json);
                row.BidType = advert.BidType; row.PaymentType = advert.PaymentType;
                row.ReceivedAtUtc = now; row.Status = "ready";
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            foreach (var (row, _) in selected) row.Status = error is WbApiException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized }
                ? "access_denied" : "source_error";
            if (error is WbApiException { RetryAfter: { } retry })
                await WbRateLimits.DeferUntilAsync(db, store.Id, "cluster_settings", 0, now.Add(retry), ct);
            logger.LogWarning("Advertising settings unavailable for store {StoreId}, {Pairs} pairs: {ErrorType}, HTTP {StatusCode}",
                store.Id, pairs.Count, error.GetType().Name, error is WbApiException wb ? (int?)wb.StatusCode : null);
        }
        foreach (var (row, _) in selected)
            db.WbResultObservations.Add(new() { Id = Guid.NewGuid(), CampaignId = row.CampaignId, AttemptedAtUtc = now,
                ReceivedAtUtc = row.Status == "ready" ? row.ReceivedAtUtc : null, State = row.Status, PayloadJson = row.PayloadJson });
        await db.SaveChangesAsync(ct);
    }

    internal static string TokenVersion(string protectedToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protectedToken)));

    private async Task RefreshProductBidsAsync(Store store,
        List<(WbAdvertisingSettings Row, AdvertisingSettingsPayload Payload)> candidates, DateTime now, CancellationToken ct)
    {
        if (candidates.Count == 0) return;
        var version = TokenVersion(store.ApiKey!);
        ProductBidSnapshot? Cached(int index) => candidates[index].Payload.ProductBids is { } snapshot &&
            snapshot.TokenVersion == version ? snapshot : null;
        void Save(int index, ProductBidSnapshot snapshot)
        {
            var (row, old) = candidates[index];
            var payload = old with { ProductBids = snapshot };
            row.PayloadJson = JsonSerializer.Serialize(payload, Json);
            candidates[index] = (row, payload);
        }

        async Task<T?> Read<T>(string method, Func<string, Task<T>> fetch) where T : class
        {
            try { return await fetch(tokens.Unprotect(store.ApiKey!)); }
            catch (Exception error) when (!ct.IsCancellationRequested &&
                error is WbApiException or HttpRequestException or IOException or JsonException or InvalidOperationException
                    or KeyNotFoundException or FormatException or OperationCanceledException or NotSupportedException or CryptographicException)
            {
                if (error is WbApiException { RetryAfter: { } retry })
                    await WbRateLimits.DeferUntilAsync(db, store.Id, method, 0, now.Add(retry), ct);
                logger.LogWarning("WB {Method} unavailable for store {StoreId}: {ErrorType}, HTTP {StatusCode}",
                    method, store.Id, error.GetType().Name, error is WbApiException wb ? (int?)wb.StatusCode : null);
                return null;
            }
        }

        var indexes = Enumerable.Range(0, candidates.Count).ToArray();
        var currentBatch = indexes.Where(i => !(Cached(i)?.CurrentAttemptedAtUtc >= now.AddMinutes(-45)))
            .OrderBy(i => Cached(i)?.CurrentAttemptedAtUtc).ThenBy(i => candidates[i].Row.CampaignId).Take(50).ToArray();
        const string currentMethod = "product_bid_current";
        if (currentBatch.Length > 0 && await WbRateLimits.TryReserveAsync(db, store.Id, currentMethod, 0, now, ct))
        {
            var ids = currentBatch.Select(i => candidates[i].Payload.Campaign!.Id).ToArray();
            var response = await Read(currentMethod, token => client.GetProductBidsAsync(token, ids, ct));
            var currentById = (response ?? []).ToDictionary(x => x.AdvertId);
            for (var n = 0; n < ids.Length; n++)
            {
                var i = currentBatch[n];
                var prior = Cached(i) ?? new(version, now, null);
                currentById.TryGetValue(ids[n], out var current);
                Save(i, prior with { CurrentAttemptedAtUtc = now, Current = current });
            }
        }

        const string minimumMethod = "product_bid_minimum";
        var selected = indexes.Where(i =>
            {
                var snapshot = Cached(i);
                var current = snapshot?.Current;
                return current != null && snapshot!.CurrentAttemptedAtUtc >= now.AddHours(-1) &&
                    current.PaymentType == candidates[i].Payload.Campaign!.PaymentType &&
                    ProductBidPlacements(current).Length > 0 &&
                    (snapshot.MinimumAttemptedAtUtc == null || snapshot.MinimumAttemptedAtUtc <= now.AddHours(-1) &&
                        (snapshot.Minimums is not { Count: > 0 } || snapshot.MinimumAttemptedAtUtc <= now.AddHours(-12) ||
                         snapshot.MinimumPaymentType != current.PaymentType ||
                         !(snapshot.MinimumPlacements ?? []).SequenceEqual(ProductBidPlacements(current))));
            })
            .OrderBy(i => Cached(i)?.MinimumAttemptedAtUtc).ThenBy(i => candidates[i].Row.CampaignId).Select(i => (int?)i)
            .FirstOrDefault();
        if (selected is { } index && await WbRateLimits.TryReserveAsync(db, store.Id, minimumMethod, 0, now, ct))
        {
            var snapshot = Cached(index)!;
            var advert = candidates[index].Payload.Campaign!;
            var placements = ProductBidPlacements(snapshot.Current!);
            var minimums = await Read(minimumMethod, token => client.GetMinimumBidsAsync(token, advert.Id,
                advert.NomenclatureIds.Distinct().ToArray(), snapshot.Current!.PaymentType, placements, ct));
            Save(index, snapshot with { MinimumAttemptedAtUtc = now, MinimumPlacements = placements,
                MinimumPaymentType = snapshot.Current!.PaymentType, Minimums = minimums });
        }
        await db.SaveChangesAsync(ct);
    }

    internal static string[] ProductBidPlacements(WbProductBidCampaign current) =>
        current.Placements.Select(x => x == "recommendations" ? "recommendation" : x)
            .Where(x => x is "search" or "recommendation" or "combined").Distinct()
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

using System.Text.Json;
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
        // Dedicated hourly slot: campaign metadata is reused from the existing campaign import.
        if (!await WbRateLimits.TryReserveAsync(db, store.Id, "cluster_settings", 0, now, ct)) return;
        var campaigns = await db.Campaigns.Where(x => x.StoreId == store.Id).ToListAsync(ct);
        var rows = await db.WbAdvertisingSettings.Where(x => campaigns.Select(c => c.Id).Contains(x.CampaignId)).ToDictionaryAsync(x => x.CampaignId, ct);
        WbAdvertisingSettings Row(Campaign campaign)
        {
            if (!rows.TryGetValue(campaign.Id, out var row))
            {
                row = new() { CampaignId = campaign.Id };
                rows.Add(campaign.Id, row); db.WbAdvertisingSettings.Add(row);
            }
            row.AttemptedAtUtc = now;
            return row;
        }
        try
        {
            var token = tokens.Unprotect(store.ApiKey!);
            // Validates Promotion + read-only token scopes as for the original import.
            tokens.ReadClaims(token);
            var payloads = rows.Values.Select(x => JsonSerializer.Deserialize<AdvertisingSettingsPayload>(x.PayloadJson, Json))
                .Where(x => x?.Campaign != null && x.CampaignReceivedAtUtc >= now.AddHours(-24)).ToArray();
            var adverts = payloads.Select(x => x!.Campaign!).ToArray();
            foreach (var campaign in campaigns)
            {
                var row = Row(campaign);
                row.Status = "unknown";
                var advert = adverts.FirstOrDefault(x => x.Id.ToString() == campaign.WbCampaignId);
                if (advert is not null) { row.BidType = advert.BidType; row.PaymentType = advert.PaymentType; }
            }
            var pairs = adverts.Where(a => campaigns.Any(c => c.WbCampaignId == a.Id.ToString()))
                .SelectMany(a => a.NomenclatureIds.Select(nm => new WbNormQueryPair(a.Id, nm))).Distinct().ToArray();
            var allBids = new List<(long Advert, AdvertisingCluster Value)>();
            var allMinus = new List<(long Advert, AdvertisingMinus Value)>();
            foreach (var chunk in pairs.Chunk(100))
            {
                using var bids = await client.GetClusterSettingsAsync(token, chunk, false, ct);
                foreach (var bid in bids.RootElement.GetProperty("bids").EnumerateArray())
                {
                    var advert = bid.GetProperty("advert_id").GetInt64();
                    var nm = bid.GetProperty("nm_id").GetInt64();
                    if (!chunk.Contains(new(advert, nm))) throw new JsonException("WB вернул незапрошенную пару.");
                    var amount = bid.GetProperty("bid").GetDecimal();
                    if (amount < 0) throw new JsonException("Отрицательная ставка WB.");
                    allBids.Add((advert, new(nm.ToString(), bid.GetProperty("norm_query").GetString()!, amount / 100m)));
                }
                using var minus = await client.GetClusterSettingsAsync(token, chunk, true, ct);
                foreach (var item in minus.RootElement.GetProperty("items").EnumerateArray())
                {
                    var advert = item.GetProperty("advert_id").GetInt64();
                    var nm = item.GetProperty("nm_id").GetInt64();
                    if (!chunk.Contains(new(advert, nm))) throw new JsonException("WB вернул незапрошенную пару.");
                    allMinus.Add((advert, new(nm.ToString(), item.GetProperty("norm_queries").EnumerateArray().Select(x => x.GetString() ?? throw new JsonException()).ToArray())));
                }
                await Task.Delay(250, ct); // Both read endpoints allow 5 requests/sec per account.
            }
            foreach (var campaign in campaigns)
            {
                var row = rows[campaign.Id];
                var advert = adverts.FirstOrDefault(x => x.Id.ToString() == campaign.WbCampaignId);
                if (advert == null || advert.BidType is not ("manual" or "unified")) continue;
                var minus = allMinus.Where(x => x.Advert == advert.Id).Select(x => x.Value).ToArray();
                // Missing pair is unknown, never interpreted as an empty exclusion list.
                if (advert.NomenclatureIds.Count == 0 || advert.NomenclatureIds.Any(n => minus.Count(m => m.NomenclatureId == n.ToString()) != 1)) continue;
                row.PayloadJson = JsonSerializer.Serialize(new AdvertisingSettingsPayload(allBids.Where(x => x.Advert == advert.Id).Select(x => x.Value).ToArray(), minus,
                    advert, payloads.First(x => x!.Campaign!.Id == advert.Id)!.CampaignReceivedAtUtc), Json);
                row.ReceivedAtUtc = now;
                row.Status = "ready";
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            foreach (var campaign in campaigns) Row(campaign).Status = error is WbApiException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized } ? "access_denied" : "source_error";
            if (error is WbApiException { RetryAfter: { } retry })
                await WbRateLimits.DeferUntilAsync(db, store.Id, "cluster_settings", 0, now.Add(retry), ct);
            logger.LogWarning("Advertising settings unavailable for store {StoreId}: {ErrorType}", store.Id, error.GetType().Name);
        }
        foreach (var row in rows.Values.Where(x => x.AttemptedAtUtc == now))
            db.WbResultObservations.Add(new() { Id = Guid.NewGuid(), CampaignId = row.CampaignId, AttemptedAtUtc = now,
                ReceivedAtUtc = row.Status == "ready" ? row.ReceivedAtUtc : null, State = row.Status, PayloadJson = row.PayloadJson });
        await db.SaveChangesAsync(ct);
    }
}

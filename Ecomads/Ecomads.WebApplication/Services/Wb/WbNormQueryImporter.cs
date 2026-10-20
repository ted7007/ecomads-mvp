using System.Globalization;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbNormQueryImporter(EcomadsDbContext db)
{
    public async Task ImportAsync(Guid storeId, IReadOnlyList<WbNormQueryPair> pairs,
        DateOnly startDate, DateOnly endDate, JsonElement root, CancellationToken cancellationToken)
    {
        var campaigns = await db.Campaigns.Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.WbCampaignId, cancellationToken);
        var articles = await db.Nomenclatures.Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.WbNomenclatureId, cancellationToken);
        var requested = pairs.ToHashSet();
        var rows = new List<WbClusterStatistic>();
        var seen = new HashSet<(long AdvertId, long NmId, DateOnly Date, string Name)>();

        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            var advertId = item.GetProperty("advertId").GetInt64();
            var nmId = item.GetProperty("nmId").GetInt64();
            if (!requested.Contains(new WbNormQueryPair(advertId, nmId)) ||
                !campaigns.TryGetValue(advertId.ToString(), out var campaign) ||
                !articles.TryGetValue(nmId.ToString(), out var article))
            {
                throw new JsonException("WB вернул незапрошенную пару кампания/артикул.");
            }
            // WB omits dailyStats for requested pairs with no statistics in the period.
            if (!item.TryGetProperty("dailyStats", out var dailyStats)) continue;
            if (dailyStats.ValueKind != JsonValueKind.Array)
                throw new JsonException("WB вернул некорректную дневную статистику кластера.");
            foreach (var day in dailyStats.EnumerateArray())
            {
                if (!DateOnly.TryParseExact(day.GetProperty("date").GetString(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new JsonException("WB вернул некорректную дату кластера.");
                if (date < startDate || date > endDate) throw new JsonException("WB вернул дату вне запрошенного периода.");
                var stat = day.GetProperty("stat");
                var name = stat.GetProperty("normQuery").GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 500 || !seen.Add((advertId, nmId, date, name)))
                    throw new JsonException("WB вернул некорректное или повторяющееся название кластера.");
                rows.Add(new WbClusterStatistic
                {
                    Id = Guid.NewGuid(), CampaignId = campaign.Id, NomenclatureId = article.Id,
                    Date = date, ClusterName = name,
                    Spend = Decimal(stat, "spend") ?? throw new JsonException("WB не вернул расход кластера."),
                    Views = Integer(stat, "views"), Clicks = Integer(stat, "clicks"),
                    Carts = Integer(stat, "atbs"), Orders = Integer(stat, "orders"),
                    OrderedProducts = Integer(stat, "shks"), AveragePosition = Decimal(stat, "avgPos"),
                    Cpc = Decimal(stat, "cpc"), Cpm = Decimal(stat, "cpm"), Ctr = Decimal(stat, "ctr")
                });
            }
        }

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            var quality = new WbQuality(db);
            var acceptedRows = new List<WbClusterStatistic>();
            foreach (var group in pairs.GroupBy(x => x.AdvertId))
            {
                var campaign = campaigns[group.Key.ToString()];
                var articleIds = group.Select(x => articles[x.NmId.ToString()].Id).ToArray();
                foreach (var articleId in articleIds)
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    var received = rows.Where(x => x.CampaignId == campaign.Id && x.NomenclatureId == articleId && x.Date == date).ToArray();
                    var previousNames = await db.WbClusterStatistics.AsNoTracking().Where(x => x.CampaignId == campaign.Id &&
                            x.NomenclatureId == articleId && x.Date == date)
                        .Select(x => x.ClusterName).ToArrayAsync(cancellationToken);
                    var missingClusters = previousNames.Except(received.Select(x => x.ClusterName), StringComparer.Ordinal).Any();
                    var state = received.Length == 0 ? "zero" : "data";
                    if (!await quality.ObserveAsync(storeId, "clusters", $"{campaign.Id}:{articleId}", date, date,
                        state, cancellationToken, missingClusters ? "Успешный ответ WB: отсутствующие строки кластеров считаются нулём." : null)) continue;
                    if (!WbQuality.Confirmed(state)) continue;
                    await db.WbClusterStatistics.Where(x => x.CampaignId == campaign.Id &&
                        x.NomenclatureId == articleId && x.Date == date).ExecuteDeleteAsync(cancellationToken);
                    acceptedRows.AddRange(received);
                }
            }
            db.WbClusterStatistics.AddRange(acceptedRows);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
        });
    }

    private static int? Integer(JsonElement stat, string property) =>
        stat.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : null;

    private static decimal? Decimal(JsonElement stat, string property) =>
        stat.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal() : null;
}

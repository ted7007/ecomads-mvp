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
            foreach (var day in item.GetProperty("dailyStats").EnumerateArray())
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
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var group in pairs.GroupBy(x => x.AdvertId))
            {
                var campaign = campaigns[group.Key.ToString()];
                var articleIds = group.Select(x => articles[x.NmId.ToString()].Id).ToArray();
                await db.WbClusterStatistics.Where(x => x.CampaignId == campaign.Id &&
                    articleIds.Contains(x.NomenclatureId) && x.Date >= startDate && x.Date <= endDate)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            db.WbClusterStatistics.AddRange(rows);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static int? Integer(JsonElement stat, string property) =>
        stat.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : null;

    private static decimal? Decimal(JsonElement stat, string property) =>
        stat.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal() : null;
}

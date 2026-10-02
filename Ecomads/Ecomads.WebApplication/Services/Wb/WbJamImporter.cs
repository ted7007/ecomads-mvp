using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbJamImporter(EcomadsDbContext db)
{
    public async Task<(int Rows, int WithData, int WithoutData)> ImportAsync(Guid storeId, IReadOnlyList<long> nmIds,
        DateOnly startDate, DateOnly endDate, JsonElement root, CancellationToken cancellationToken, Guid? jobId = null)
    {
        var articles = await db.Nomenclatures.Where(x => x.StoreId == storeId)
            .ToDictionaryAsync(x => x.WbNomenclatureId, cancellationToken);
        var requested = nmIds.ToHashSet();
        var seen = new HashSet<(long NmId, string Text)>();
        var rows = new List<WbJamSearchQuery>();
        var loadedAt = DateTime.UtcNow;
        foreach (var item in root.GetProperty("data").GetProperty("items").EnumerateArray())
        {
            var nmId = item.GetProperty("nmId").GetInt64();
            var text = item.GetProperty("text").GetString()?.Trim();
            if (!requested.Contains(nmId) || !articles.TryGetValue(nmId.ToString(), out var article) ||
                string.IsNullOrWhiteSpace(text) || text.Length > 500 || !seen.Add((nmId, text.ToLowerInvariant())))
                throw new JsonException("Отчёт Джема WB содержит незапрошенный товар или некорректный запрос.");
            rows.Add(new WbJamSearchQuery
            {
                Id = Guid.NewGuid(), StoreId = storeId, NomenclatureId = article.Id,
                StartDate = startDate, EndDate = endDate, SearchText = text,
                Frequency = CurrentLong(item, "frequency"), WeekFrequency = Long(item, "weekFrequency"),
                AveragePosition = CurrentDecimal(item, "avgPosition"),
                MedianPosition = CurrentDecimal(item, "medianPosition"),
                OpenCard = CurrentLong(item, "openCard"), AddToCart = CurrentLong(item, "addToCart"),
                Orders = CurrentLong(item, "orders"), LoadedAtUtc = loadedAt
            });
        }

        var articleIds = nmIds.Select(x => articles[x.ToString()].Id).ToArray();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.WbJamSearchQueries.Where(x => x.StoreId == storeId &&
                articleIds.Contains(x.NomenclatureId) && x.StartDate == startDate && x.EndDate == endDate)
                .ExecuteDeleteAsync(cancellationToken);
            db.WbJamSearchQueries.AddRange(rows);
            var existingChecks = await db.WbJamArticleChecks.Where(x => x.StoreId == storeId &&
                articleIds.Contains(x.NomenclatureId) && x.StartDate == startDate && x.EndDate == endDate)
                .ToDictionaryAsync(x => x.NomenclatureId, cancellationToken);
            var withData = rows.Select(x => x.NomenclatureId).ToHashSet();
            foreach (var articleId in articleIds)
            {
                if (!existingChecks.TryGetValue(articleId, out var check))
                {
                    check = new WbJamArticleCheck { StoreId = storeId, NomenclatureId = articleId,
                        StartDate = startDate, EndDate = endDate };
                    db.WbJamArticleChecks.Add(check);
                }
                check.JobId = jobId;
                check.CheckedAtUtc = loadedAt;
                check.HasData = withData.Contains(articleId);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        var countWithData = rows.Select(x => x.NomenclatureId).Distinct().Count();
        return (rows.Count, countWithData, articleIds.Length - countWithData);
    }

    private static long? CurrentLong(JsonElement item, string property) =>
        item.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.Object &&
        field.TryGetProperty("current", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64() : null;

    private static decimal? CurrentDecimal(JsonElement item, string property) =>
        item.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.Object &&
        field.TryGetProperty("current", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal() : null;

    private static long? Long(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64() : null;
}

using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbCoverageDay(DateOnly Date, decimal? Spend, decimal? Orders, decimal? Drr,
    int CheckedCampaigns, int ExpectedCampaigns, int CheckedStores, int ExpectedStores);

public sealed record WbCoveragePeriod(IReadOnlyList<WbCoverageDay> Days, int ConfirmedDays,
    decimal? Drr, string Status, string? Reason, DateTime? LastCheckedAtUtc);

public sealed class WbDataCoverageService(EcomadsDbContext db)
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    public async Task<WbCoveragePeriod> GetAsync(Guid sellerId, DateOnly start, DateOnly end,
        Guid? campaignId, CancellationToken ct)
    {
        var stores = await db.Stores.AsNoTracking().Where(x => x.SellerId == sellerId && x.ApiKey != null)
            .Select(x => x.Id).ToArrayAsync(ct);
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => stores.Contains(x.StoreId) &&
                (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11) &&
                (!campaignId.HasValue || x.Id == campaignId.Value))
            .Select(x => new { x.Id, x.WbCreatedAtUtc, x.WbDeletedAtUtc, x.WbStatus })
            .ToArrayAsync(ct);
        var campaignIds = campaigns.Select(x => x.Id).ToArray();
        var checks = await db.WbCampaignDailyChecks.AsNoTracking().Where(x =>
                campaignIds.Contains(x.CampaignId) && x.Date >= start && x.Date <= end)
            .Select(x => new { x.CampaignId, x.Date, x.Result, x.Spend, x.CheckedAtUtc })
            .ToArrayAsync(ct);
        var checkMap = checks.ToDictionary(x => (x.CampaignId, x.Date));
        var expenses = campaignId.HasValue ? [] : await db.WbStoreDailySpends.AsNoTracking().Where(x =>
                stores.Contains(x.StoreId) && x.Date >= start && x.Date <= end)
            .Select(x => new { x.StoreId, x.Date, x.Spend, x.LoadedAtUtc }).ToArrayAsync(ct);
        var expenseMap = expenses.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToArray());
        var orders = campaignId.HasValue ? [] : await db.WbStoreDailyOrders.AsNoTracking().Where(x =>
                stores.Contains(x.StoreId) && x.Date >= start && x.Date <= end)
            .Select(x => new { x.StoreId, x.Date, x.OrderSum }).ToArrayAsync(ct);
        var orderMap = orders.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToArray());
        var days = new List<WbCoverageDay>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var expected = campaigns.Where(x =>
            {
                var first = x.WbCreatedAtUtc.HasValue ? DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(x.WbCreatedAtUtc.Value, Moscow)) : DateOnly.MinValue;
                var last = x.WbStatus == 7 && x.WbDeletedAtUtc.HasValue ? DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(x.WbDeletedAtUtc.Value, Moscow)) : DateOnly.MaxValue;
                return first <= date && last >= date;
            }).ToArray();
            var verified = expected.Select(x => checkMap.GetValueOrDefault((x.Id, date)))
                .Where(x => x != null && (x.Result is "data" or "zero") && x.Spend.HasValue).ToArray();
            decimal? spend;
            if (campaignId.HasValue)
                spend = stores.Length > 0 && verified.Length == expected.Length
                    ? verified.Sum(x => x!.Spend!.Value) : null;
            else
            {
                var expenseRows = expenseMap.GetValueOrDefault(date) ?? [];
                spend = stores.Length > 0 &&
                    expenseRows.Select(x => x.StoreId).Distinct().Count() == stores.Length
                        ? expenseRows.Sum(x => x.Spend) : null;
            }
            var orderRows = orderMap.GetValueOrDefault(date) ?? [];
            decimal? orderSum = !campaignId.HasValue && stores.Length > 0 &&
                orderRows.Select(x => x.StoreId).Distinct().Count() == stores.Length
                    ? orderRows.Sum(x => x.OrderSum) : null;
            var drr = spend.HasValue && orderSum > 0 ? spend.Value / orderSum.Value * 100m : (decimal?)null;
            days.Add(new WbCoverageDay(date, spend, orderSum, drr, verified.Length, expected.Length,
                orderRows.Select(x => x.StoreId).Distinct().Count(), stores.Length));
        }
        var confirmed = days.Where(x => x.Spend.HasValue && x.Orders.HasValue).ToArray();
        var denominator = confirmed.Sum(x => x.Orders!.Value);
        var drrTotal = denominator > 0 ? confirmed.Sum(x => x.Spend!.Value) / denominator * 100m : (decimal?)null;
        var status = confirmed.Length == days.Count && drrTotal.HasValue ? "complete" :
            drrTotal.HasValue ? "preliminary" : "unavailable";
        var missingSpend = days.Count(x => !x.Spend.HasValue);
        var missingOrders = days.Count(x => !x.Orders.HasValue);
        var reason = status == "complete" ? null : confirmed.Length == 0
            ? $"Нет дней с одновременно подтверждёнными расходом и заказами. " +
              $"Расход по истории затрат WB не загружен за {missingSpend} из {days.Count} дней, заказы — за {missingOrders}." : denominator == 0
                ? "За подтверждённые дни сумма заказов равна нулю." :
                $"Подтверждено {confirmed.Length} из {days.Count} дней.";
        return new WbCoveragePeriod(days, confirmed.Length, drrTotal, status, reason,
            campaignId.HasValue ? checks.Length == 0 ? null : checks.Max(x => x.CheckedAtUtc) :
                expenses.Length == 0 ? null : expenses.Max(x => x.LoadedAtUtc));
    }
}

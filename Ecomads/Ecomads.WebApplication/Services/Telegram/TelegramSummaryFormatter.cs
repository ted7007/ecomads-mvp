using System.Globalization;
using System.Text;

namespace Ecomads.WebApplication.Services.Telegram;

public sealed record TelegramSummaryCampaign(string Name, decimal Spend, long Orders, decimal? Drr);

public sealed record TelegramSummary(
    string StoreName,
    DateOnly ReportDate,
    decimal TotalSpend,
    decimal TotalRevenue,
    long TotalOrders,
    IReadOnlyList<TelegramSummaryCampaign> Campaigns,
    string? UpdatedAtText,
    string DashboardUrl,
    bool IsTest = false,
    IReadOnlyList<string>? MissingCampaignNames = null,
    DateOnly? ReportEndDate = null,
    string DashboardLinkLabel = "Открыть сводку за этот день",
    IReadOnlyList<string>? SourceWarnings = null, decimal? StoreSpend = null, decimal? StoreOrderSum = null);

/// <summary>
/// Builds the plain-text yesterday summary. Plain text on purpose: no parse_mode means nothing to escape,
/// and campaign names with special characters are always delivered as typed.
/// </summary>
public static class TelegramSummaryFormatter
{
    public const int MaxMessageLength = 4096;
    public const int MaxCampaigns = 10;
    private const int MaxNameLength = 40;

    private static readonly NumberFormatInfo Numbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        PercentDecimalSeparator = ","
    };

    public static string Format(TelegramSummary summary)
    {
        var shown = summary.Campaigns.Take(MaxCampaigns).ToList();
        while (true)
        {
            var text = Build(summary, shown);
            if (text.Length <= MaxMessageLength || shown.Count == 0) return Truncate(text, MaxMessageLength);
            shown.RemoveAt(shown.Count - 1);
        }
    }

    public static string FormatMoney(decimal value) => value.ToString("N2", Numbers);

    public static string FormatDrr(decimal? value) => value.HasValue ? value.Value.ToString("F1", Numbers) + "%" : "—";

    public static decimal? TotalDrr(decimal spend, decimal revenue) => revenue > 0 ? spend * 100m / revenue : null;

    private static string Build(TelegramSummary summary, IReadOnlyList<TelegramSummaryCampaign> campaigns)
    {
        var sb = new StringBuilder();
        if (summary.IsTest) sb.AppendLine("ТЕСТОВОЕ СООБЩЕНИЕ · данные ниже демонстрационные").AppendLine();
        sb.AppendLine($"EcomAds · {Shorten(summary.StoreName, 60)}");
        sb.AppendLine(summary.ReportEndDate is { } end && end != summary.ReportDate
            ? $"Рекламная сводка за {summary.ReportDate:dd.MM.yyyy} – {end:dd.MM.yyyy}, МСК"
            : $"Рекламная сводка за {summary.ReportDate:dd.MM.yyyy}, МСК");
        sb.AppendLine();
        if (summary.SourceWarnings is { Count: > 0 })
        {
            sb.AppendLine("Сводка предварительная. Неподтверждённые показатели обозначены прочерком.");
            foreach (var warning in summary.SourceWarnings) sb.AppendLine(Shorten(warning, 200));
            sb.AppendLine();
        }
        if (summary.MissingCampaignNames is { Count: > 0 } missing)
        {
            sb.AppendLine(summary.ReportEndDate is { } periodEnd && periodEnd != summary.ReportDate
                ? "Сводка с пропусками: часть дней кампаний не подтверждена."
                : $"Сводка с пропусками: подтверждено {summary.Campaigns.Count} из {summary.Campaigns.Count + missing.Count} кампаний.");
            sb.AppendLine("WB не вернул данные по остальным кампаниям. Пропуски не считаются нулями; рекламные итоги ниже — только по подтверждённым данным.");
            foreach (var name in missing.Take(5)) sb.AppendLine($"• {Shorten(name, 80)}");
            if (missing.Count > 5) sb.AppendLine($"И ещё {missing.Count - 5} кампаний.");
            sb.AppendLine();
        }
        sb.AppendLine(summary.StoreSpend.HasValue ? $"Расход кабинета: {FormatMoney(summary.StoreSpend.Value)} ₽" : "Расход кабинета: —");
        sb.AppendLine(summary.StoreOrderSum.HasValue ? $"Все заказы, сумма: {FormatMoney(summary.StoreOrderSum.Value)} ₽" : "Все заказы, сумма: —");
        var storeDrr = summary.StoreSpend.HasValue && summary.StoreOrderSum > 0
            ? summary.StoreSpend.Value / summary.StoreOrderSum.Value * 100m : (decimal?)null;
        sb.AppendLine($"Общий ДРР: {FormatDrr(storeDrr)}");
        sb.AppendLine();
        if (summary.Campaigns.Count == 0 && summary.MissingCampaignNames is { Count: > 0 })
            sb.AppendLine("Рекламные показатели: WB не вернул подтверждённых данных.");
        else
        {
            sb.AppendLine($"Расход на рекламу: {FormatMoney(summary.TotalSpend)} ₽");
            sb.AppendLine($"Заказы из рекламы: {summary.TotalOrders.ToString("N0", Numbers)}");
            sb.AppendLine($"Рекламная выручка: {FormatMoney(summary.TotalRevenue)} ₽");
            sb.AppendLine($"Рекламный ДРР: {FormatDrr(TotalDrr(summary.TotalSpend, summary.TotalRevenue))}");
        }
        sb.AppendLine();
        sb.AppendLine($"Кампании по расходу — топ {campaigns.Count}");
        for (var i = 0; i < campaigns.Count; i++)
        {
            var c = campaigns[i];
            sb.AppendLine($"{i + 1}. {Shorten(c.Name, MaxNameLength)}: {FormatMoney(c.Spend)} ₽ · " +
                          $"{c.Orders.ToString("N0", Numbers)} заказов · ДРР {FormatDrr(c.Drr)}");
        }
        sb.AppendLine();
        sb.AppendLine($"Данные: {summary.UpdatedAtText ?? "время обновления неизвестно"}");
        sb.AppendLine($"{summary.DashboardLinkLabel}: {summary.DashboardUrl}");
        sb.Append("Отключить сообщения: /stop");
        return sb.ToString();
    }

    private static string Shorten(string? value, int max)
    {
        var text = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (text.Length == 0) return "Без названия";
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

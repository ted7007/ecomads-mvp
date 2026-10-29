using System.Globalization;
using System.Text;

namespace Ecomads.WebApplication.Services.Telegram;

public sealed record TelegramSummaryCampaign(string Name, decimal Spend, long Orders, decimal? Drr);

/// <summary>Previous-day values for comparison. A field is null when the day was not fully confirmed.</summary>
public sealed record TelegramPrevious(decimal? StoreSpend, decimal? StoreOrderSum, decimal? AdSpend, decimal? AdRevenue);

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
    long? TotalClicks = null,
    long? TotalImpressions = null,
    TelegramPrevious? Previous = null,
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

    /// <summary>The same summary with Telegram HTML formatting (bold, emoji, comparison with the previous day).</summary>
    public static string FormatHtml(TelegramSummary summary)
    {
        var shown = summary.Campaigns.Take(MaxCampaigns).ToList();
        while (true)
        {
            var text = BuildHtml(summary, shown);
            if (text.Length <= MaxMessageLength || shown.Count == 0) return text.Length <= MaxMessageLength ? text : Truncate(TelegramHtml.ToPlain(text), MaxMessageLength);
            shown.RemoveAt(shown.Count - 1);
        }
    }

    private static string Esc(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Delta(decimal current, decimal? previous)
    {
        if (previous is not > 0) return string.Empty;
        var change = (current - previous.Value) / previous.Value * 100m;
        var arrow = change > 0 ? "▲ +" : change < 0 ? "▼ −" : "= ";
        return $" ({arrow}{Math.Abs(change).ToString("F1", Numbers)}% к пред. дню)";
    }

    private static string DrrDelta(decimal? current, decimal? previous)
    {
        if (!current.HasValue || !previous.HasValue) return string.Empty;
        var change = current.Value - previous.Value;
        var arrow = change > 0 ? "▲ +" : change < 0 ? "▼ −" : "= ";
        return $" ({arrow}{Math.Abs(change).ToString("F1", Numbers)} п.п. к пред. дню)";
    }

    private static string BuildHtml(TelegramSummary summary, IReadOnlyList<TelegramSummaryCampaign> campaigns)
    {
        var sb = new StringBuilder();
        var period = summary.ReportEndDate is { } end && end != summary.ReportDate;
        var previous = period ? null : summary.Previous;
        if (summary.IsTest) sb.AppendLine("ТЕСТОВОЕ СООБЩЕНИЕ 🧪 · данные ниже демонстрационные").AppendLine();
        sb.AppendLine($"📊 <b>EcomAds · {Esc(Shorten(summary.StoreName, 60))}</b>");
        sb.AppendLine("<blockquote>" + (period
            ? $"Рекламная сводка за {summary.ReportDate:dd.MM.yyyy} – {summary.ReportEndDate:dd.MM.yyyy}, МСК"
            : $"Рекламная сводка за {summary.ReportDate:dd.MM.yyyy}, МСК") + "</blockquote>");
        if (summary.SourceWarnings is { Count: > 0 })
        {
            sb.AppendLine("⚠️ Сводка предварительная. Неподтверждённые показатели обозначены прочерком.");
            foreach (var warning in summary.SourceWarnings) sb.AppendLine(Esc(Shorten(warning, 200)));
            sb.AppendLine();
        }
        if (summary.MissingCampaignNames is { Count: > 0 } missing)
        {
            sb.AppendLine(period
                ? "⚠️ Сводка с пропусками: часть дней кампаний не подтверждена."
                : $"⚠️ Сводка с пропусками: подтверждено {summary.Campaigns.Count} из {summary.Campaigns.Count + missing.Count} кампаний.");
            sb.AppendLine("WB не вернул данные по остальным кампаниям. Пропуски не считаются нулями; рекламные итоги ниже — только по подтверждённым данным.");
            foreach (var name in missing.Take(5)) sb.AppendLine($"• {Esc(Shorten(name, 80))}");
            if (missing.Count > 5) sb.AppendLine($"И ещё {missing.Count - 5} кампаний.");
            sb.AppendLine();
        }
        sb.AppendLine("🏪 <b>Кабинет</b>");
        sb.AppendLine(summary.StoreSpend.HasValue
            ? $"Расход кабинета: <b>{FormatMoney(summary.StoreSpend.Value)} ₽</b>{Delta(summary.StoreSpend.Value, previous?.StoreSpend)}"
            : "Расход кабинета: <b>—</b>");
        sb.AppendLine(summary.StoreOrderSum.HasValue
            ? $"Все заказы, сумма: <b>{FormatMoney(summary.StoreOrderSum.Value)} ₽</b>{Delta(summary.StoreOrderSum.Value, previous?.StoreOrderSum)}"
            : "Все заказы, сумма: <b>—</b>");
        var storeDrr = summary.StoreSpend.HasValue && summary.StoreOrderSum > 0
            ? summary.StoreSpend.Value / summary.StoreOrderSum.Value * 100m : (decimal?)null;
        var prevStoreDrr = previous?.StoreSpend.HasValue == true && previous.StoreOrderSum > 0
            ? previous.StoreSpend!.Value / previous.StoreOrderSum.Value * 100m : (decimal?)null;
        sb.AppendLine($"Общий ДРР: <b>{FormatDrr(storeDrr)}</b>{DrrDelta(storeDrr, prevStoreDrr)}");
        sb.AppendLine();
        sb.AppendLine("📢 <b>Реклама</b>");
        if (summary.Campaigns.Count == 0 && summary.MissingCampaignNames is { Count: > 0 })
            sb.AppendLine("Рекламные показатели: WB не вернул подтверждённых данных.");
        else
        {
            var drr = TotalDrr(summary.TotalSpend, summary.TotalRevenue);
            var prevDrr = previous?.AdSpend.HasValue == true && previous.AdRevenue.HasValue
                ? TotalDrr(previous.AdSpend!.Value, previous.AdRevenue.Value) : null;
            sb.AppendLine($"Расход на рекламу: <b>{FormatMoney(summary.TotalSpend)} ₽</b>{Delta(summary.TotalSpend, previous?.AdSpend)}");
            sb.AppendLine($"Заказы из рекламы: <b>{summary.TotalOrders.ToString("N0", Numbers)}</b>");
            sb.AppendLine($"Рекламная выручка: <b>{FormatMoney(summary.TotalRevenue)} ₽</b>{Delta(summary.TotalRevenue, previous?.AdRevenue)}");
            sb.AppendLine($"Рекламный ДРР: <b>{FormatDrr(drr)}</b>{DrrDelta(drr, prevDrr)}");
            if (summary.TotalImpressions > 0 && summary.TotalClicks.HasValue)
                sb.AppendLine($"CTR: <b>{(summary.TotalClicks.Value * 100m / summary.TotalImpressions.Value).ToString("F1", Numbers)}%</b>");
        }
        sb.AppendLine();
        AppendComparisonTable(sb, summary, previous);
        sb.AppendLine($"🏆 <b>Кампании по расходу — топ {campaigns.Count}</b>");
        // Expandable quote: three lines are visible, the rest opens with a tap, so the message stays short.
        if (campaigns.Count > 0) sb.Append("<blockquote expandable>");
        for (var i = 0; i < campaigns.Count; i++)
        {
            var c = campaigns[i];
            if (i > 0) sb.AppendLine();
            sb.Append($"{i + 1}. <b>{Esc(Shorten(c.Name, MaxNameLength))}</b>: {FormatMoney(c.Spend)} ₽ · " +
                      $"{c.Orders.ToString("N0", Numbers)} заказов · ДРР {FormatDrr(c.Drr)}");
        }
        if (campaigns.Count > 0) sb.AppendLine("</blockquote>");
        sb.AppendLine();
        sb.AppendLine($"🕒 Данные: {Esc(summary.UpdatedAtText ?? "время обновления неизвестно")}");
        sb.AppendLine($"{Esc(summary.DashboardLinkLabel)}: {Esc(summary.DashboardUrl)}");
        sb.Append("Отключить сообщения: /stop");
        return sb.ToString();
    }

    /// <summary>Monospace table "yesterday / day before / change". Shown only when there is a previous day to compare with.</summary>
    private static void AppendComparisonTable(StringBuilder sb, TelegramSummary summary, TelegramPrevious? previous)
    {
        if (previous == null) return;
        var rows = new List<(string Name, string Now, string Before, string Change)>();
        void Money(string name, decimal? now, decimal? before)
        {
            if (!now.HasValue || before is not > 0) return;
            var change = (now.Value - before.Value) / before.Value * 100m;
            rows.Add((name, FormatRub(now.Value), FormatRub(before.Value),
                (change > 0 ? "+" : change < 0 ? "−" : string.Empty) + Math.Abs(change).ToString("F0", Numbers) + "%"));
        }
        Money("Расход", summary.StoreSpend, previous.StoreSpend);
        Money("Заказы", summary.StoreOrderSum, previous.StoreOrderSum);
        Money("Реклама", summary.TotalSpend, previous.AdSpend);
        if (summary.StoreSpend.HasValue && summary.StoreOrderSum > 0 && previous.StoreSpend.HasValue && previous.StoreOrderSum > 0)
        {
            var now = summary.StoreSpend.Value / summary.StoreOrderSum.Value * 100m;
            var before = previous.StoreSpend.Value / previous.StoreOrderSum.Value * 100m;
            var diff = now - before;
            rows.Add(("ДРР", FormatDrr(now), FormatDrr(before),
                (diff > 0 ? "+" : diff < 0 ? "−" : string.Empty) + Math.Abs(diff).ToString("F1", Numbers) + " п.п."));
        }
        if (rows.Count == 0) return;
        const int labelWidth = 8;
        var nowWidth = Math.Max(5, rows.Max(x => x.Now.Length));
        var beforeWidth = Math.Max(7, rows.Max(x => x.Before.Length));
        sb.AppendLine("📋 <b>Сравнение с предыдущим днём</b>");
        sb.AppendLine("<pre>" + "Показ.".PadRight(labelWidth) + "Вчера".PadLeft(nowWidth + 1) + "Позавч.".PadLeft(beforeWidth + 2) + "  Δ");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            sb.Append(r.Name.PadRight(labelWidth) + r.Now.PadLeft(nowWidth + 1) + r.Before.PadLeft(beforeWidth + 2) + "  " + r.Change);
            sb.AppendLine(i == rows.Count - 1 ? "</pre>" : string.Empty);
        }
        sb.AppendLine();
    }

    public static string FormatRub(decimal value) => value.ToString("N0", Numbers);

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

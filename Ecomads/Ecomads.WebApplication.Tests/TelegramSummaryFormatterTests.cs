using Ecomads.WebApplication.Services.Telegram;
using Xunit;

namespace Ecomads.WebApplication.Tests;

public sealed class TelegramSummaryFormatterTests
{
    private const string Url = "https://ecomads.ru/dashboard?startDate=2026-10-12&endDate=2026-10-12";

    private static TelegramSummary Sample(IReadOnlyList<TelegramSummaryCampaign> campaigns,
        decimal revenue = 20000m, bool isTest = false) =>
        new("Кабинет", new DateOnly(2026, 10, 12), campaigns.Sum(x => x.Spend), revenue,
            campaigns.Sum(x => x.Orders), campaigns, "13.10.2026 06:12 МСК", Url, isTest);

    [Fact]
    public void AccountDrrUsesOnlyConfirmedAccountValues()
    {
        var confirmed = Sample([new("Campaign", 50, 1, 5)]) with { StoreSpend = 100, StoreOrderSum = 1000 };
        Assert.Contains("Общий ДРР: 10,0%", TelegramSummaryFormatter.Format(confirmed));
        Assert.Contains("Общий ДРР: —", TelegramSummaryFormatter.Format(confirmed with { StoreSpend = null }));
        Assert.Contains("Общий ДРР: —", TelegramSummaryFormatter.Format(confirmed with { StoreOrderSum = null }));
        Assert.Contains("Общий ДРР: —", TelegramSummaryFormatter.Format(confirmed with { StoreOrderSum = 0 }));
        Assert.Contains("Общий ДРР: 0,0%", TelegramSummaryFormatter.Format(confirmed with { StoreSpend = 0 }));
    }

    [Fact]
    public void ShowsTotalsCampaignsAndLinks()
    {
        var text = TelegramSummaryFormatter.Format(Sample([new("Пледы осень", 3900m, 7, 21m)]));

        Assert.Contains("Рекламная сводка за 12.10.2026, МСК", text);
        Assert.Contains("Расход на рекламу: 3 900,00 ₽", text);
        Assert.Contains("Заказы из рекламы: 7", text);
        Assert.Contains("Рекламный ДРР: 19,5%", text);
        Assert.Contains("1. Пледы осень: 3 900,00 ₽ · 7 заказов · ДРР 21,0%", text);
        Assert.Contains("Данные: 13.10.2026 06:12 МСК", text);
        Assert.Contains("Кампании по расходу — топ 1", text);
        Assert.Contains("Открыть сводку за этот день: " + Url, text);
        Assert.Contains(Url, text);
        Assert.EndsWith("Отключить сообщения: /stop", text);
    }

    [Fact]
    public void UndefinedDrrIsADashNotZero()
    {
        var text = TelegramSummaryFormatter.Format(Sample([new("Без выручки", 100m, 0, null)], revenue: 0m));

        Assert.Contains("Рекламный ДРР: —", text);
        Assert.Contains("ДРР —", text);
        Assert.DoesNotContain("0,0%", text);
    }

    [Fact]
    public void LongNamesAndManyCampaignsStayWithinTelegramLimit()
    {
        var campaigns = Enumerable.Range(1, 30)
            .Select(i => new TelegramSummaryCampaign(new string('Я', 300) + i, 1000m + i, i, 10m)).ToList();

        var text = TelegramSummaryFormatter.Format(Sample(campaigns));

        Assert.True(text.Length <= TelegramSummaryFormatter.MaxMessageLength);
        Assert.Contains("Кампании по расходу — топ ", text);
        Assert.Contains("…", text);
    }

    [Fact]
    public void SpecialCharactersAreDeliveredAsTyped()
    {
        var text = TelegramSummaryFormatter.Format(Sample([new("A&B <i>x</i> *_[]", 10m, 1, 5m)]));

        Assert.Contains("A&B <i>x</i> *_[]", text);
    }

    [Fact]
    public void TestMessageIsClearlyLabeled()
    {
        var text = TelegramSummaryFormatter.Format(Sample([new("Демо", 10m, 1, 5m)], isTest: true));

        Assert.StartsWith("ТЕСТОВОЕ СООБЩЕНИЕ", text);
    }

    [Fact]
    public void CompletelySparseAdvertisingIsUnknownRatherThanZero()
    {
        var text = TelegramSummaryFormatter.Format(Sample([]) with
        {
            MissingCampaignNames = ["Нет данных"]
        });
        Assert.Contains("Сводка с пропусками", text);
        Assert.Contains("Рекламные показатели: WB не вернул подтверждённых данных", text);
        Assert.DoesNotContain("Расход на рекламу: 0,00", text);
        Assert.EndsWith("Отключить сообщения: /stop", text);
    }

    [Fact]
    public void PeriodSummaryShowsBothDatesAndPeriodLink()
    {
        var summary = Sample([new("Пледы", 100m, 1, 10m)]) with
        {
            ReportEndDate = new DateOnly(2026, 10, 14), DashboardLinkLabel = "Открыть сводку за этот период"
        };

        var text = TelegramSummaryFormatter.Format(summary);

        Assert.Contains("Рекламная сводка за 12.10.2026 – 14.10.2026, МСК", text);
        Assert.Contains("Открыть сводку за этот период: ", text);
    }
}

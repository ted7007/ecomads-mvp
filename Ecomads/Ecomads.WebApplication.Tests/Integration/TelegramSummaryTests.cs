using System.Security.Claims;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Telegram;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class TelegramSummaryTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task YesterdaySummaryRequiresCollectionAndDoesNotSendTwice(bool sparse, bool uncertainExpense)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB", ApiKey = "test-token" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Test", WbCampaignId = "123", WbStatus = 9 };
        var chat = new TelegramChat { Id = Guid.NewGuid(), SellerId = seller.Id, ChatId = 123456, LinkedAtUtc = DateTime.UtcNow };
        db.AddRange(seller, store, campaign, chat);
        await db.SaveChangesAsync();

        var bot = new FakeBot();
        var controller = new TelegramController(db, bot, new WbReportReadinessService(db)) { ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, seller.Id.ToString())], "test")) }
        } };
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("example.test");

        Assert.IsType<ConflictObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Empty(bot.Messages);

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var at = DateTime.UtcNow;
        store.CampaignsRefreshedAtUtc = at;
        db.WbCampaignDailyChecks.Add(new WbCampaignDailyCheck { CampaignId = campaign.Id, Date = yesterday,
            CheckedAtUtc = at, Result = "data", Spend = 100m });
        db.WbStoreDailySpends.Add(new WbStoreDailySpend { StoreId = store.Id, Date = yesterday, Spend = 100m, LoadedAtUtc = at });
        db.WbStoreDailyOrders.Add(new WbStoreDailyOrders { StoreId = store.Id, Date = yesterday, OrderSum = 1000m, LoadedAtUtc = at });
        db.CampaignStatistics.Add(new CampaignStatistics { CampaignId = campaign.Id,
            Date = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            Spend = 100m, Revenue = 500m, Orders = 2 });
        if (uncertainExpense) db.WbSourceQualities.Add(new() { StoreId = store.Id, Source = "expenses",
            StartDate = yesterday, EndDate = yesterday, State = "error", Reason = "WB не подтвердил расход.",
            LastAttemptAtUtc = at, AcceptedAtUtc = at.AddHours(-1), PreviousValue = 100 });
        if (sparse)
        {
            var paused = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Missing paused campaign",
                WbCampaignId = "456", WbStatus = 11 };
            db.Campaigns.Add(paused);
            db.WbCampaignDailyChecks.Add(new() { CampaignId = paused.Id, Date = yesterday, Result = "data",
                Spend = 9999m, CheckedAtUtc = DateTime.UtcNow.AddDays(-1),
                LastAttemptAtUtc = DateTime.UtcNow, LastAttemptResult = "no_data" });
            db.CampaignStatistics.Add(new() { CampaignId = paused.Id,
                Date = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), Spend = 9999m });
        }
        await db.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Single(bot.Messages);
        Assert.Contains("100,00", bot.Messages[0]);
        Assert.Contains(uncertainExpense ? "Общий ДРР: —" : "Общий ДРР: 10,0%", bot.Messages[0]);
        if (uncertainExpense) Assert.Contains("Расход кабинета: —", bot.Messages[0]);
        Assert.Contains("Рекламный ДРР: 20,0%", bot.Messages[0]);
        if (sparse)
        {
            Assert.Contains("Сводка с пропусками", bot.Messages[0]);
            Assert.Contains("Missing paused campaign", bot.Messages[0]);
            Assert.DoesNotContain("9999", bot.Messages[0]);
            Assert.DoesNotContain("9 999", bot.Messages[0]);
        }
        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Single(bot.Messages);
        Assert.Single(await db.TelegramDeliveries.ToListAsync());
    }

    private sealed class FakeBot : ITelegramBotClient
    {
        public bool IsConfigured => true;
        public string? BotUsername => "testbot";
        public List<string> Messages { get; } = [];
        public Task<IReadOnlyList<TelegramIncoming>> GetUpdatesAsync(long offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TelegramIncoming>>([]);
        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            Messages.Add(text);
            return Task.CompletedTask;
        }
    }
}

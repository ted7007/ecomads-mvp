using System.Security.Claims;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Telegram;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class TelegramDeliveryRetryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ConfirmedFailureIsRetriedAndSentIsNotRepeated()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var (controller, store, chat, bot) = await SeedAsync(db);
        bot.NextError = new TelegramApiException(500);

        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Empty(bot.Messages);
        Assert.Equal("failed", (await Deliveries(db)).Status);

        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Single(bot.Messages);
        Assert.Equal("sent", (await Deliveries(db)).Status);

        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Single(bot.Messages);
        Assert.True((await db.TelegramChats.AsNoTracking().SingleAsync(x => x.Id == chat.Id)).IsActive);
    }

    [Fact]
    public async Task UnknownOutcomeIsNeverRetriedAutomatically()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var (controller, store, _, bot) = await SeedAsync(db);
        bot.NextError = new HttpRequestException("timeout");

        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Equal("unknown", (await Deliveries(db)).Status);

        // The bot would succeed now, but a possibly delivered message must not be duplicated.
        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Empty(bot.Messages);
    }

    [Fact]
    public async Task BlockedBotDeactivatesTheChat()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var (controller, store, chat, bot) = await SeedAsync(db);
        bot.NextError = new TelegramApiException(403);

        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));

        Assert.False((await db.TelegramChats.AsNoTracking().SingleAsync(x => x.Id == chat.Id)).IsActive);
        Assert.Equal("failed", (await Deliveries(db)).Status);
    }

    [Fact]
    public async Task TestMessageNeedsNoWbCabinetAndIsLabeledAsDemo()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var chat = new TelegramChat { Id = Guid.NewGuid(), SellerId = seller.Id, ChatId = 777, LinkedAtUtc = DateTime.UtcNow };
        db.AddRange(seller, chat);
        await db.SaveChangesAsync();
        var bot = new FlakyBot();
        var controller = CreateController(db, bot, seller.Id);

        Assert.IsType<OkObjectResult>(await controller.SendTest(CancellationToken.None));

        Assert.Single(bot.Messages);
        Assert.StartsWith("ТЕСТОВОЕ СООБЩЕНИЕ", bot.Messages[0]);
        Assert.Empty(await db.TelegramDeliveries.ToListAsync());
    }

    private static Task<TelegramDelivery> Deliveries(EcomadsDbContext db) =>
        db.TelegramDeliveries.AsNoTracking().SingleAsync();

    private static TelegramController CreateController(EcomadsDbContext db, FlakyBot bot, Guid sellerId)
    {
        var controller = new TelegramController(db, bot, new Ecomads.WebApplication.Services.Wb.WbReportReadinessService(db)) { ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, sellerId.ToString())], "test")) }
        } };
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("example.test");
        return controller;
    }

    private static async Task<(TelegramController Controller, Store Store, TelegramChat Chat, FlakyBot Bot)> SeedAsync(
        EcomadsDbContext db)
    {
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB", ApiKey = "test-token" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Test", WbCampaignId = "123", WbStatus = 9 };
        var chat = new TelegramChat { Id = Guid.NewGuid(), SellerId = seller.Id, ChatId = 123456, LinkedAtUtc = DateTime.UtcNow };
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        db.AddRange(seller, store, campaign, chat);
        var at = DateTime.UtcNow;
        store.CampaignsRefreshedAtUtc = at;
        db.WbCampaignDailyChecks.Add(new WbCampaignDailyCheck { CampaignId = campaign.Id, Date = yesterday,
            CheckedAtUtc = at, Result = "data", Spend = 100m });
        db.WbStoreDailySpends.Add(new WbStoreDailySpend { StoreId = store.Id, Date = yesterday, Spend = 100m, LoadedAtUtc = at });
        db.WbStoreDailyOrders.Add(new WbStoreDailyOrders { StoreId = store.Id, Date = yesterday, LoadedAtUtc = at });
        db.CampaignStatistics.Add(new CampaignStatistics { CampaignId = campaign.Id,
            Date = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            Spend = 100m, Revenue = 500m, Orders = 2 });
        await db.SaveChangesAsync();
        var bot = new FlakyBot();
        return (CreateController(db, bot, seller.Id), store, chat, bot);
    }

    private sealed class FlakyBot : ITelegramBotClient
    {
        public bool IsConfigured => true;
        public string? BotUsername => "testbot";
        public List<string> Messages { get; } = [];
        public Exception? NextError { get; set; }

        public Task<IReadOnlyList<TelegramIncoming>> GetUpdatesAsync(long offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TelegramIncoming>>([]);

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            if (NextError is { } error)
            {
                NextError = null;
                throw error;
            }
            Messages.Add(text);
            return Task.CompletedTask;
        }
    }
}

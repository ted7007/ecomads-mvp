using System.Security.Claims;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Telegram;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class TelegramSummaryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task YesterdaySummaryRequiresCoverageAndDoesNotSendTwice()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "WB", ApiKey = "test-token" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Test", WbCampaignId = "123", WbStatus = 9 };
        var chat = new TelegramChat { Id = Guid.NewGuid(), SellerId = seller.Id, ChatId = 123456, LinkedAtUtc = DateTime.UtcNow };
        db.AddRange(seller, store, campaign, chat);
        await db.SaveChangesAsync();

        var bot = new FakeBot();
        var controller = new TelegramController(db, bot) { ControllerContext = new ControllerContext
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
        db.WbSyncJobs.Add(new WbSyncJob { Id = Guid.NewGuid(), StoreId = store.Id, StartDate = yesterday,
            EndDate = yesterday, CampaignIdsJson = "[123]", Status = "completed", Kind = "fullstats" });
        db.CampaignStatistics.Add(new CampaignStatistics { CampaignId = campaign.Id,
            Date = DateTime.SpecifyKind(yesterday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            Spend = 100m, Revenue = 500m, Orders = 2 });
        await db.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await controller.SendYesterday(store.Id, CancellationToken.None));
        Assert.Single(bot.Messages);
        Assert.Contains("100,00", bot.Messages[0]);
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

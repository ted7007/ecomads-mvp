using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Tests.Wb;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbJournalTests(PostgresFixture postgres)
{
    [Fact]
    public async Task HttpResponsesAreDurableBeforeParsingIncludingErrorsAndTimeouts()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Journal" };
        db.Sellers.Add(seller); db.Stores.Add(store); await db.SaveChangesAsync();
        var clock = new WbTestClock(new DateTimeOffset(2026, 10, 14, 1, 0, 0, TimeSpan.Zero));
        using var services = new ServiceCollection().AddDbContext<EcomadsDbContext>(options =>
            options.UseNpgsql(db.Database.GetConnectionString())).BuildServiceProvider();
        byte[] interruptedBytes = [0x5b, 0x7b, 0x22, 0xd0, 0x00];
        using var scenario = new WbHttpScenario().Reply("[{broken").Reply("quota", HttpStatusCode.TooManyRequests, 70)
            .Reply("denied", HttpStatusCode.Unauthorized).Reply("denied", HttpStatusCode.Forbidden)
            .Reply("upstream", HttpStatusCode.BadGateway).Fail(new TaskCanceledException("timeout")).Interrupt(interruptedBytes);
        using var capture = new WbCaptureHandler(services.GetRequiredService<IServiceScopeFactory>(), clock) { InnerHandler = scenario };
        using var http = new HttpClient(capture) { BaseAddress = new Uri("https://wb.invalid") };
        var client = new WbPromotionClient(http);
        var day = new DateOnly(2026, 10, 13);
        var job = new WbSyncJob { StoreId = store.Id, Kind = "expenses", StartDate = day, EndDate = day };
        using var context = new WbIngestionContext(job, clock.Now.UtcDateTime);
        await Assert.ThrowsAnyAsync<JsonException>(() => client.GetCostsAsync("secret-must-not-be-stored", day, day, default));
        var limit = await Assert.ThrowsAsync<WbApiException>(() => client.GetCostsAsync("secret-must-not-be-stored", day, day, default));
        Assert.Equal(TimeSpan.FromSeconds(70), limit.RetryAfter);
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.BadGateway })
            Assert.Equal(code, (await Assert.ThrowsAsync<WbApiException>(() => client.GetCostsAsync("secret-must-not-be-stored", day, day, default))).StatusCode);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCostsAsync("secret-must-not-be-stored", day, day, default));
        var interrupted = await Record.ExceptionAsync(() => client.GetCostsAsync("secret-must-not-be-stored", day, day, default));
        Assert.True(interrupted is IOException or HttpRequestException);
        var saved = await db.WbResponseSnapshots.AsNoTracking().ToArrayAsync();
        Assert.Equal(7, saved.Length); Assert.Equal(7, scenario.Requests.Count);
        Assert.Contains(saved, x => x.Body == "[{broken" && x.State == "received");
        Assert.Contains(saved, x => x.Reason == "transport_error");
        Assert.Contains(saved, x => x.RawBodyBase64 != null && x.GetBodyBytes().SequenceEqual(interruptedBytes));
        foreach (var snapshot in saved)
        {
            Assert.DoesNotContain("secret-must-not-be-stored", JsonSerializer.Serialize(snapshot));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(snapshot.GetBodyBytes())), snapshot.Sha256);
            Assert.Equal(clock.Now.UtcDateTime, snapshot.RequestedAtUtc);
        }
    }

    [Fact]
    public async Task Replay_PreservesMultiplicityBoundaryRowsAndRejectsOlderSnapshot()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Journal" };
        db.Sellers.Add(seller); db.Stores.Add(store); await db.SaveChangesAsync();
        var day = new DateOnly(2026, 10, 13);
        var at = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var job = new WbSyncJob { StoreId = store.Id, Kind = "expenses", StartDate = day, EndDate = day, CampaignIdsJson = "[]" };
        async Task<Guid> Save(string body, DateTime requested)
        {
            var snapshot = new WbResponseSnapshot { Id = Guid.NewGuid(), StoreId = store.Id, Kind = job.Kind,
                JobStateJson = JsonSerializer.Serialize(job), RequestedAtUtc = requested,
                ReceivedAtUtc = at.AddHours(1), HttpStatus = 200, Body = body,
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))) };
            db.WbResponseSnapshots.Add(snapshot); await db.SaveChangesAsync(); return snapshot.Id;
        }
        using var services = new ServiceCollection().AddSingleton(new WbCostsImporter(db)).BuildServiceProvider();
        var processor = new WbResponseProcessor(db, services, TimeProvider.System);
        var snapshotId = await Save("""
            [{"updTime":"2026-10-13T10:00:00Z","updSum":10},
             {"updTime":"2026-10-13T10:00:00Z","updSum":10},
             {"updTime":"2026-10-13T22:30:00Z","updSum":7}]
            """, at);
        await processor.ApplyAsync(snapshotId, false, default);
        await processor.ApplyAsync(snapshotId, false, default);
        Assert.Equal(20, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        Assert.Equal(3, await db.WbCostObservations.CountAsync());
        var boundary = await db.WbCostObservations.SingleAsync(x => x.State == "adjacent");
        Assert.Equal(day.AddDays(1), boundary.MoscowDate);
        Assert.Equal(7, boundary.Amount);
        var older = await Save("""[{"updTime":"2026-10-13T10:00:00Z","updSum":500}]""", at.AddMinutes(-1));
        await processor.ApplyAsync(older, false, default);
        Assert.Equal(20, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        var lower = await Save("""[{"updTime":"2026-10-13T10:00:00Z","updSum":5}]""", at.AddMinutes(1));
        await processor.ApplyAsync(lower, false, default);
        Assert.Equal(20, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        var quality = await db.WbSourceQualities.SingleAsync(x => x.Source == "expenses");
        Assert.Equal("revision", quality.State); Assert.Equal(5, quality.ProposedValue);
        var ambiguous = await Save("""[{"updTime":"2026-10-13T10:00:00","updSum":0}]""", at.AddMinutes(2));
        await processor.ApplyAsync(ambiguous, false, default);
        Assert.Equal("ambiguous", (await db.WbSourceQualities.SingleAsync(x => x.Source == "expenses")).State);
        Assert.Equal(20, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        job.StartDate = day.AddDays(1); job.EndDate = day.AddDays(1);
        var nextDay = await Save("[]", at.AddMinutes(3)); // WB does not repeat the boundary operation.
        await processor.ApplyAsync(nextDay, false, default);
        Assert.Equal(7, (await db.WbCostObservations.SingleAsync(x => x.MoscowDate == day.AddDays(1))).Amount);
        Assert.Equal(7, (await db.WbSourceQualities.SingleAsync(x => x.Source == "expenses_boundary")).ProposedValue);
        Assert.Equal("ambiguous", (await db.WbSourceQualities.SingleAsync(x => x.Source == "expenses" && x.StartDate == day.AddDays(1))).State);
        Assert.Empty(await db.WbStoreDailySpends.Where(x => x.Date == day.AddDays(1)).ToArrayAsync());
    }

    [Theory]
    [InlineData("[]", "no_operations", null)]
    [InlineData("[{\"updTime\":\"2026-10-13T10:00:00Z\",\"updSum\":0}]", "zero", 0)]
    [InlineData("[{\"updTime\":\"2026-10-13T10:00:00Z\"}]", "ambiguous", null)]
    public async Task EmptyMissingAndExplicitZeroAreDifferent(string body, string expected, int? amount)
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Journal" };
        db.Sellers.Add(seller); db.Stores.Add(store); await db.SaveChangesAsync();
        using var response = JsonDocument.Parse(body);
        var day = new DateOnly(2026, 10, 13);
        await new WbCostsImporter(db).ImportAsync(store.Id, day, day, response.RootElement, default);
        Assert.Equal(expected, (await db.WbSourceQualities.SingleAsync()).State);
        Assert.Equal(amount.HasValue ? (decimal?)amount.Value : null, (await db.WbStoreDailySpends.SingleOrDefaultAsync())?.Spend);
    }
}

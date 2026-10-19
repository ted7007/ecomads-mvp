using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Ecomads.WebApplication.Tests.Wb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbIngestionPublicationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RetentionKeepsUnresolvedAcceptedAndPageAncestorsButExpiresSupersededErrors()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var owner = TestData.CreateRegularSeller(); var now = DateTime.UtcNow; var old = now.AddDays(-100);
        var day = DateOnly.FromDateTime(old); var store = new Store { Id = Guid.NewGuid(), SellerId = owner.Id, Name = "Retention" };
        db.Sellers.Add(owner); db.Stores.Add(store);
        var job = new WbSyncJob { StoreId = store.Id, Kind = "funnel_month", StartDate = day, EndDate = day };
        var parent = Snapshot(job, old, "{}"); parent.State = "applied";
        job.PairIdsJson = JsonSerializer.Serialize(new WbFunnelPageState(SnapshotIds: [parent.Id]));
        var accepted = Snapshot(job, old, "{}"); accepted.State = "applied";
        job.PairIdsJson = null;
        var unresolved = Snapshot(job, old, "{}"); unresolved.State = "partial";
        var superseded = Snapshot(job, old, "{}"); superseded.State = "applied";
        var error = Snapshot(job, old, "{}"); error.State = "error";
        var received = Snapshot(job, old, "{}"); received.State = "received";
        var decisionSource = Snapshot(job, old, "{}"); decisionSource.State = "applied";
        db.WbResponseSnapshots.AddRange(parent, accepted, unresolved, superseded, error, received, decisionSource);
        db.WbSourceQualities.Add(new() { StoreId = store.Id, Source = "orders", StartDate = day, EndDate = day,
            State = "ambiguous", SnapshotId = unresolved.Id, AcceptedSnapshotId = accepted.Id, LastAttemptAtUtc = old });
        db.WbIngestionDecisions.Add(new() { Id = Guid.NewGuid(), StoreId = store.Id, SellerId = owner.Id,
            Date = day, SnapshotId = decisionSource.Id, PreviousValue = 2, AcceptedValue = 1, DecidedAtUtc = old });
        await db.SaveChangesAsync();
        await WbJournalRetentionWorker.PruneAsync(db, now, default);
        var kept = await db.WbResponseSnapshots.AsNoTracking().Select(x => x.Id).ToArrayAsync();
        Assert.Equal(5, kept.Length);
        Assert.Contains(parent.Id, kept); Assert.Contains(accepted.Id, kept); Assert.Contains(unresolved.Id, kept);
        Assert.Contains(received.Id, kept); Assert.Contains(decisionSource.Id, kept);
        Assert.DoesNotContain(superseded.Id, kept); Assert.DoesNotContain(error.Id, kept);
    }

    private static WbResponseSnapshot Snapshot(WbSyncJob job, DateTime at, string body) => new()
    {
        Id = Guid.NewGuid(), StoreId = job.StoreId, JobId = job.Id == Guid.Empty ? null : job.Id,
        Kind = job.Kind, JobStateJson = JsonSerializer.Serialize(job), RequestedAtUtc = at,
        ReceivedAtUtc = at.AddSeconds(1), HttpStatus = 200, Body = body,
        Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))
    };

    [Fact]
    public async Task RecalculationRequiresOwnerCurrentSnapshotAndUnambiguousDate()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var owner = TestData.CreateRegularSeller(); var other = TestData.CreateRegularSeller();
        var at = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc); var date = new DateOnly(2026, 10, 13);
        var store = new Store { Id = Guid.NewGuid(), SellerId = owner.Id, Name = "Journal", ApiKey = "protected-test", CampaignsRefreshedAtUtc = at };
        db.Sellers.AddRange(owner, other); db.Stores.Add(store);
        db.WbStoreDailyOrders.Add(new() { StoreId = store.Id, Date = date, OrderSum = 100, LoadedAtUtc = at });
        var job = new WbSyncJob { StoreId = store.Id, Kind = "expenses", StartDate = date, EndDate = date };
        var first = Snapshot(job, at, """[{"updTime":"2026-10-13T10:00:00Z","updSum":20}]""");
        var lower = Snapshot(job, at.AddMinutes(1), """[{"updTime":"2026-10-13T10:00:00Z","updSum":5}]""");
        db.WbResponseSnapshots.AddRange(first, lower); await db.SaveChangesAsync();
        using var services = new ServiceCollection().AddSingleton(new WbCostsImporter(db)).BuildServiceProvider();
        var clock = new WbTestClock(new DateTimeOffset(at.AddHours(2)));
        var processor = new WbResponseProcessor(db, services, clock);
        await processor.ApplyAsync(first.Id, false, default); await processor.ApplyAsync(lower.Id, false, default);
        var readiness = await new WbReportReadinessService(db, clock).GetAsync(store.Id, date, default);
        Assert.True(readiness.Collected); Assert.False(readiness.Ready);
        var coverage = await new WbDataCoverageService(db).GetAsync(owner.Id, date, date, null, default);
        Assert.Null(coverage.Days[0].Spend); Assert.Null(coverage.Drr);
        WbIngestionController Controller(Guid seller) => new(db, processor, clock) { ControllerContext = new() {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, seller.ToString())], "test")) } } };
        var intruder = Controller(other.Id);
        Assert.IsType<NotFoundResult>(await intruder.Snapshot(store.Id, lower.Id, default));
        Assert.IsType<NotFoundResult>(await intruder.Replay(store.Id, lower.Id, default));
        Assert.IsType<NotFoundResult>(await intruder.Accept(store.Id, date, new(lower.Id), default));
        var controller = Controller(owner.Id);
        Assert.IsType<ConflictObjectResult>(await controller.Accept(store.Id, date, new(first.Id), default));
        Assert.IsType<OkObjectResult>(await controller.Accept(store.Id, date, new(lower.Id), default));
        Assert.IsType<ConflictObjectResult>(await controller.Accept(store.Id, date, new(lower.Id), default));
        var decision = await db.WbIngestionDecisions.SingleAsync();
        Assert.Equal(20, decision.PreviousValue); Assert.Equal(5, decision.AcceptedValue); Assert.Equal(owner.Id, decision.SellerId);
        await processor.ApplyAsync(lower.Id, false, default);
        Assert.Equal("accepted", (await db.WbSourceQualities.SingleAsync()).State);
        Assert.True((await new WbReportReadinessService(db, clock).GetAsync(store.Id, date, default)).Ready);
        var invalid = Snapshot(job, at.AddMinutes(2), """[{"updTime":"2026-10-13T10:00:00","updSum":1}]""");
        db.WbResponseSnapshots.Add(invalid); await db.SaveChangesAsync();
        await processor.ApplyAsync(invalid.Id, false, default);
        Assert.IsType<ConflictObjectResult>(await controller.Accept(store.Id, date, new(invalid.Id), default));
        Assert.Equal(5, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        Assert.Single(await db.WbIngestionDecisions.ToArrayAsync());
    }

    [Fact]
    public async Task DatabaseFailureRollsBackPublicationAndJobButRetainsResponseForRestart()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller(); var at = DateTime.UtcNow; var day = new DateOnly(2026, 10, 13);
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Journal", ApiKey = "protected-test" };
        db.Sellers.Add(seller); db.Stores.Add(store);
        var job = new WbSyncJob { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "expenses", StartDate = day, EndDate = day,
            PairIdsJson = JsonSerializer.Serialize(WbSyncPlanner.ExpenseHistoryBatches(day, day, new HashSet<DateOnly>())),
            CreatedAtUtc = at, UpdatedAtUtc = at, NextAttemptAtUtc = at };
        db.WbSyncJobs.Add(job);
        var snapshot = Snapshot(job, at, """[{"updTime":"2026-10-13T10:00:00Z","updSum":123}]""");
        db.WbResponseSnapshots.Add(snapshot); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_test_spend() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'simulated database failure'; END $$;
            CREATE TRIGGER reject_test_spend BEFORE INSERT ON wb_store_daily_spend
            FOR EACH ROW EXECUTE FUNCTION reject_test_spend();
            """);
        using var services = new ServiceCollection().AddSingleton(new WbCostsImporter(db)).BuildServiceProvider();
        var processor = new WbResponseProcessor(db, services, TimeProvider.System);
        await Assert.ThrowsAsync<DbUpdateException>(() => processor.ApplyAsync(snapshot.Id, true, default));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.WbStoreDailySpends.ToArrayAsync());
        Assert.Empty(await db.WbSourceQualities.ToArrayAsync());
        Assert.Empty(await db.WbCostObservations.ToArrayAsync());
        Assert.Equal(0, (await db.WbSyncJobs.SingleAsync()).NextCampaignOffset);
        Assert.Null((await db.WbResponseSnapshots.SingleAsync()).AppliedAtUtc);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_spend ON wb_store_daily_spend; DROP FUNCTION reject_test_spend();");
        using var restartedServices = new ServiceCollection()
            .AddDbContext<Ecomads.WebApplication.Data.EcomadsDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()))
            .AddScoped<WbCostsImporter>().BuildServiceProvider();
        // No HTTP client/token service is registered: restarting must use the saved response.
        var worker = new WbSyncWorker(restartedServices.GetRequiredService<IServiceScopeFactory>(), NullLogger<WbSyncWorker>.Instance);
        await worker.ProcessNextAsync(default);
        await worker.ProcessNextAsync(default);
        db.ChangeTracker.Clear();
        Assert.Equal(123, (await db.WbStoreDailySpends.SingleAsync()).Spend);
        Assert.Equal("completed", (await db.WbSyncJobs.SingleAsync()).Status);
        Assert.Single(await db.WbSyncJobEvents.ToArrayAsync());
    }
}

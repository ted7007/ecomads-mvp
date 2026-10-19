using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Ecomads.WebApplication.Tests.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbLegacyRecheckTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RecheckUsesNormalQueueOnceAndOnlyForEnabledStore()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var connection = db.Database.GetConnectionString()!;
        var seller = TestData.CreateRegularSeller();
        var selected = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Selected", ApiKey = "test", AutoRefreshEnabled = true };
        var other = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Other", ApiKey = "test", AutoRefreshEnabled = true };
        var clock = new WbTestClock(new DateTimeOffset(2026, 10, 15, 22, 0, 0, TimeSpan.Zero));
        var end = new DateOnly(2026, 10, 15); var start = end.AddDays(-30);
        db.Sellers.Add(seller); db.Stores.AddRange(selected, other);
        foreach (var store in new[] { selected, other }) db.WbSourceQualities.Add(new() {
            StoreId = store.Id, Source = "expenses", StartDate = end, EndDate = end, State = "legacy",
            LastAttemptAtUtc = clock.Now.AddDays(-1).UtcDateTime });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Wb:Ingestion:StoreIds"] = selected.Id.ToString(), ["Wb:Ingestion:RecheckLegacyDays"] = "31" }).Build();
        var rollout = new WbIngestionRollout(config);
        using var services = new ServiceCollection().AddDbContext<EcomadsDbContext>(o => o.UseNpgsql(connection))
            .AddSingleton(rollout).AddScoped(p => new WbSyncPlanner(p.GetRequiredService<EcomadsDbContext>(),
                null!, null!, NullLogger<WbSyncPlanner>.Instance, clock, rollout)).BuildServiceProvider();
        var worker = new WbLegacyRecheckWorker(services.GetRequiredService<IServiceScopeFactory>(), config, clock,
            NullLogger<WbLegacyRecheckWorker>.Instance);
        await Task.WhenAll(worker.RunOnceAsync(default), worker.RunOnceAsync(default));
        var job = await db.WbSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(selected.Id, job.StoreId); Assert.Equal("expenses_month", job.Kind);
        Assert.Equal(start, job.StartDate); Assert.Equal(end, job.EndDate);
        Assert.Equal(2, JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson!)!.Length);
        var expenseParts = JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson!)!;
        Assert.Equal(end, expenseParts[0].EndDate);
        Assert.Equal(end.AddDays(-28), expenseParts[0].StartDate);
        Assert.Empty(await db.WbMethodSlots.ToArrayAsync());
        Assert.Single(await db.WbRefreshSchedules.ToArrayAsync());
        await worker.RunOnceAsync(default);
        Assert.Single(await db.WbSyncJobs.ToArrayAsync());
        config["Wb:Ingestion:StoreIds"] = "";
        await worker.RunOnceAsync(default);
        Assert.Equal(2, await db.WbSyncJobs.CountAsync());
        Assert.Single(await db.WbSyncJobs.Where(x => x.StoreId == other.Id).ToArrayAsync());
    }

    [Fact]
    public async Task RecentOrdersAreQueuedOnceEvenWhenMonthlyHistoryIsAlreadyRunning()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var connection = db.Database.GetConnectionString()!;
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Recent first",
            ApiKey = "test", AutoRefreshEnabled = true };
        var clock = new WbTestClock(new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.Zero));
        var end = new DateOnly(2026, 10, 18);
        var monthly = new WbSyncJob { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "funnel_month",
            StartDate = end.AddDays(-30), EndDate = end, CampaignIdsJson = "[20260918,20260920]",
            Status = "running", NextCampaignOffset = 1, CreatedAtUtc = clock.Now.AddHours(-1).UtcDateTime };
        db.Sellers.Add(seller); db.Stores.Add(store); db.WbSyncJobs.Add(monthly);
        db.WbSourceQualities.Add(new() { StoreId = store.Id, Source = "orders", StartDate = end,
            EndDate = end, State = "legacy", LastAttemptAtUtc = clock.Now.AddDays(-1).UtcDateTime });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Wb:Ingestion:StoreIds"] = store.Id.ToString(), ["Wb:Ingestion:RecheckLegacyDays"] = "31" }).Build();
        var rollout = new WbIngestionRollout(config);
        using var services = new ServiceCollection().AddDbContext<EcomadsDbContext>(o => o.UseNpgsql(connection))
            .AddSingleton(rollout).AddScoped(p => new WbSyncPlanner(p.GetRequiredService<EcomadsDbContext>(),
                null!, null!, NullLogger<WbSyncPlanner>.Instance, clock, rollout)).BuildServiceProvider();
        var worker = new WbLegacyRecheckWorker(services.GetRequiredService<IServiceScopeFactory>(), config, clock,
            NullLogger<WbLegacyRecheckWorker>.Instance);
        await Task.WhenAll(worker.RunOnceAsync(default), worker.RunOnceAsync(default));
        var recent = await db.WbSyncJobs.AsNoTracking().SingleAsync(x => x.Kind == "funnel_recent");
        Assert.Equal(end.AddDays(-6), recent.StartDate); Assert.Equal(end, recent.EndDate);
        Assert.Equal("pending", recent.Status);
        Assert.Equal(2, await db.WbSyncJobs.CountAsync());
        Assert.Empty(await db.WbMethodSlots.ToArrayAsync());
        // A finished or failed attempt is not duplicated every minute while legacy rows remain.
        await db.WbSyncJobs.Where(x => x.Id == recent.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, "failed"));
        await worker.RunOnceAsync(default);
        Assert.Equal(2, await db.WbSyncJobs.CountAsync());
        Assert.Equal(1, (await db.WbSyncJobs.AsNoTracking().SingleAsync(x => x.Id == monthly.Id)).NextCampaignOffset);
    }
}

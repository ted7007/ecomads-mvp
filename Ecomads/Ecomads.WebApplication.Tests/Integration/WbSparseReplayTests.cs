using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class WbSparseReplayTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ReplaysSavedSuccessWithoutCallingWbOrAdvancingJobsAndDoesNotLoopOnBadSnapshot()
    {
        await using var db = await postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Replay" };
        var campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, Name = "C", WbCampaignId = "123" };
        db.AddRange(seller, store, campaign);
        var day = WbSyncPlanner.Yesterday();
        var at = DateTime.UtcNow.AddHours(-1);
        var job = new WbSyncJob { Id = Guid.NewGuid(), StoreId = store.Id, Kind = "fullstats", Status = "completed",
            StartDate = day, EndDate = day, CampaignIdsJson = "[123]", NextCampaignOffset = 0 };
        db.Add(job);
        WbResponseSnapshot Snapshot(string body, int http = 200) => new() {
            Id = Guid.NewGuid(), StoreId = store.Id, JobId = job.Id, Kind = "fullstats", Version = "2", State = "partial",
            RequestedAtUtc = at, ReceivedAtUtc = at.AddSeconds(1), AppliedAtUtc = at.AddSeconds(2), HttpStatus = http,
            Body = body, Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))), JobStateJson = JsonSerializer.Serialize(job)
        };
        var saved = Snapshot("[]");
        var broken = Snapshot("{}");
        var denied = Snapshot("[]", 403);
        db.AddRange(saved, broken, denied);
        db.WbCampaignDailyChecks.Add(new() { CampaignId = campaign.Id, Date = day, Result = "unknown", LastAttemptResult = "no_data", LastAttemptAtUtc = at });
        await db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddDbContext<EcomadsDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()));
        services.AddScoped<WbFullStatsImporter>();
        services.AddScoped<WbResponseProcessor>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        using var provider = services.BuildServiceProvider(); // No WB client is registered.
        var worker = new WbSparseReplayWorker(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            new WbIngestionRollout(new ConfigurationBuilder().Build()), NullLogger<WbSparseReplayWorker>.Instance);
        Assert.Equal(2, await worker.RunOnceAsync(default));
        db.ChangeTracker.Clear();
        Assert.Equal("zero", (await db.WbCampaignDailyChecks.SingleAsync()).Result);
        Assert.Equal(0, (await db.WbCampaignDailyChecks.SingleAsync()).Spend);
        Assert.Equal("zero", (await db.WbSourceQualities.SingleAsync()).State);
        Assert.Equal(WbIngestionVersion.Current, (await db.WbResponseSnapshots.FindAsync(saved.Id))!.Version);
        Assert.Equal("replay_failed", (await db.WbResponseSnapshots.FindAsync(broken.Id))!.State);
        Assert.Equal("2", (await db.WbResponseSnapshots.FindAsync(denied.Id))!.Version);
        Assert.Equal(0, (await db.WbSyncJobs.SingleAsync()).NextCampaignOffset);
        Assert.Equal(0, await worker.RunOnceAsync(default));
    }
}

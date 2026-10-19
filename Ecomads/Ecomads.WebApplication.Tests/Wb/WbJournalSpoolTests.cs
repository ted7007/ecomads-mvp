using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbJournalSpoolTests
{
    [Fact]
    public async Task DatabaseFailureLeavesOriginalResponseOnDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ecomads-journal-test-" + Guid.NewGuid().ToString("N"));
        var snapshot = new WbResponseSnapshot { Id = Guid.NewGuid(), StoreId = Guid.NewGuid(), Body = "[{\"updSum\":5}]" };
        snapshot.Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.Body)));
        var path = Path.Combine(directory, $"{snapshot.Id:N}.json");
        using var services = new ServiceCollection().BuildServiceProvider(); // Deliberately unavailable database.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Wb:Journal:SpoolPath"] = directory }).Build();
        using var journal = new WbResponseJournal(services.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<WbResponseJournal>.Instance);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => journal.StoreAsync(snapshot));
            Assert.True(File.Exists(path));
            var saved = JsonSerializer.Deserialize<WbResponseSnapshot>(await File.ReadAllTextAsync(path))!;
            Assert.Equal(snapshot.Body, saved.Body);
            Assert.Equal(snapshot.StoreId, saved.StoreId);
            Assert.Equal(snapshot.Sha256, saved.Sha256);
            await Assert.ThrowsAsync<InvalidOperationException>(() => journal.RecoverAsync(default));
            Assert.True(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public async Task CorruptFileIsRetainedForInspection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ecomads-journal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Wb:Journal:SpoolPath"] = directory }).Build();
        using var journal = new WbResponseJournal(services.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<WbResponseJournal>.Instance);
        try
        {
            await File.WriteAllTextAsync(path, "{truncated");
            await journal.RecoverAsync(default);
            Assert.Equal("{truncated", await File.ReadAllTextAsync(path));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new WbResponseSnapshot { Id = Guid.NewGuid(), Body = "altered", Sha256 = "wrong" }));
            await journal.RecoverAsync(default);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}

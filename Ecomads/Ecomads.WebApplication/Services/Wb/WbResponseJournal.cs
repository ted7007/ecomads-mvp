using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

// This directory must live on a persistent volume, independent of the application image.
public sealed class WbResponseJournal(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<WbResponseJournal> logger) : BackgroundService
{
    private string DirectoryPath => configuration["Wb:Journal:SpoolPath"] ??
        Path.Combine(AppContext.BaseDirectory, "wb-journal");

    public async Task StoreAsync(WbResponseSnapshot snapshot)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DirectoryPath);
        else Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(DirectoryPath, $"{snapshot.Id:N}.json");
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        var options = new FileStreamOptions {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.WriteThrough | FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var file = new FileStream(temporary, options))
        {
            await file.WriteAsync(bytes);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path);
        await PublishAsync(snapshot, CancellationToken.None);
        File.Delete(path);
    }

    private async Task PublishAsync(WbResponseSnapshot snapshot, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Capture and spool recovery can race; an identical snapshot is inserted once.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"wb-response:" + snapshot.Id}, 0))", ct);
            var existing = await db.WbResponseSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == snapshot.Id, ct);
            if (existing == null)
            {
                db.WbResponseSnapshots.Add(snapshot);
                await db.SaveChangesAsync(ct);
            }
            else if (existing.Sha256 != snapshot.Sha256 || existing.StoreId != snapshot.StoreId)
                throw new InvalidOperationException("Сохранённый ответ WB не совпадает с журналом на диске.");
            await tx.CommitAsync(ct);
        });
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        if (!Directory.Exists(DirectoryPath)) return;
        // A crash during rename can leave a complete temporary file. Incomplete files stay
        // available for diagnosis and are never deleted on a deserialization failure.
        foreach (var path in Directory.EnumerateFiles(DirectoryPath).Where(x => x.EndsWith(".json") ||
                     x.EndsWith(".json.tmp") && File.GetLastWriteTimeUtc(x) < DateTime.UtcNow.AddMinutes(-5)).Take(100))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                WbResponseSnapshot? snapshot;
                await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    snapshot = await JsonSerializer.DeserializeAsync<WbResponseSnapshot>(file, cancellationToken: ct);
                if (snapshot == null) throw new JsonException("Пустой файл журнала WB.");
                if (snapshot.Sha256 != Convert.ToHexString(SHA256.HashData(snapshot.GetBodyBytes())))
                    throw new JsonException("Контрольная сумма файла журнала WB не совпадает.");
                await PublishAsync(snapshot, ct);
                File.Delete(path);
            }
            catch (IOException) { /* Still being written, or temporarily inaccessible: retry later. */ }
            catch (Exception error) when (error is JsonException or FormatException)
            { logger.LogError(error, "WB spool file {Path} requires inspection", path); }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RecoverAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "WB spool recovery failed; files retained"); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}

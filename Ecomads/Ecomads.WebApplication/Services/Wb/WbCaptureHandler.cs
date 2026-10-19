using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbCaptureHandler(IServiceScopeFactory scopes, TimeProvider clock, WbResponseJournal? journal = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var context = WbIngestionContext.Current;
        if (context == null) return await base.SendAsync(request, ct);
        var snapshot = new WbResponseSnapshot { Id = Guid.NewGuid(), StoreId = context.Job.StoreId,
            JobId = context.Job.Id == Guid.Empty ? null : context.Job.Id, Kind = context.Job.Kind,
            JobStateJson = JsonSerializer.Serialize(context.Job), Attempt = context.Job.AttemptCount + 1,
            Page = context.Job.NextCampaignOffset, RequestedAtUtc = clock.GetUtcNow().UtcDateTime,
            RequestUri = request.RequestUri!.PathAndQuery,
            RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct) };
        HttpResponseMessage? response = null;
        using var received = new MemoryStream();
        try
        {
            response = await base.SendAsync(request, ct);
            snapshot.HttpStatus = (int)response.StatusCode;
            var originalContent = response.Content;
            await using (var stream = await originalContent.ReadAsStreamAsync(ct))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await stream.ReadAsync(buffer, ct)) != 0)
                    received.Write(buffer, 0, count);
            }
            var bufferedContent = new ByteArrayContent(received.ToArray());
            foreach (var header in originalContent.Headers)
                bufferedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = bufferedContent;
            originalContent.Dispose();
            if (!response.IsSuccessStatusCode) { snapshot.State = "error"; snapshot.Reason = $"http_{snapshot.HttpStatus}"; }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        { snapshot.State = "error"; snapshot.Reason = "transport_error"; response?.Dispose(); throw; }
        finally
        {
            // Retain even a truncated body when reading the HTTP stream fails.
            var originalBytes = received.ToArray();
            snapshot.Body = Encoding.UTF8.GetString(originalBytes).Replace('\0', '\ufffd');
            if (!Encoding.UTF8.GetBytes(snapshot.Body).AsSpan().SequenceEqual(originalBytes))
                snapshot.RawBodyBase64 = Convert.ToBase64String(originalBytes);
            snapshot.ReceivedAtUtc = clock.GetUtcNow().UtcDateTime;
            snapshot.Sha256 = Convert.ToHexString(SHA256.HashData(originalBytes));
            // Independent DbContext: parse/import rollback cannot erase the original response.
            if (journal != null) await journal.StoreAsync(snapshot);
            else
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
                db.WbResponseSnapshots.Add(snapshot);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            context.SnapshotId = snapshot.Id; context.RequestedAtUtc = snapshot.RequestedAtUtc;
            context.ReceivedAtUtc = snapshot.ReceivedAtUtc;
        }
        return response!;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecomads.WebApplication.Services.Wb;

public interface IWbOperationsClient
{
    Task<JsonDocument> StocksAsync(string token, bool fbs, int offset, CancellationToken ct);
    Task<JsonDocument> BudgetsAsync(string token, long[] ids, CancellationToken ct);
    Task<JsonDocument> BalanceAsync(string token, CancellationToken ct);
    Task<JsonDocument> DemandAsync(string token, DateOnly start, DateOnly end, int offset, CancellationToken ct);
    Task<JsonDocument> CreateFbwReportAsync(string token, CancellationToken ct) => throw new NotSupportedException();
    Task<JsonDocument> FbwReportStatusAsync(string token, Guid taskId, CancellationToken ct) => throw new NotSupportedException();
    Task<JsonDocument> DownloadFbwReportAsync(string token, Guid taskId, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Only read endpoints, including read-only POST queries. No WB mutation methods.</summary>
public sealed class WbOperationsClient(IHttpClientFactory factory) : IWbOperationsClient
{
    public Task<JsonDocument> CreateFbwReportAsync(string token, CancellationToken ct) =>
        Read("wb-operations-analytics", token, HttpMethod.Get,
            "/api/v1/warehouse_remains?locale=en&groupByNm=true&groupByBarcode=true&groupBySize=true", null, ct);
    public Task<JsonDocument> FbwReportStatusAsync(string token, Guid taskId, CancellationToken ct) =>
        Read("wb-operations-analytics", token, HttpMethod.Get, $"/api/v1/warehouse_remains/tasks/{taskId:D}/status", null, ct);
    public Task<JsonDocument> DownloadFbwReportAsync(string token, Guid taskId, CancellationToken ct) =>
        Read("wb-operations-analytics", token, HttpMethod.Get, $"/api/v1/warehouse_remains/tasks/{taskId:D}/download", null, ct);
    public Task<JsonDocument> StocksAsync(string token, bool fbs, int offset, CancellationToken ct) =>
        Read("wb-operations-analytics", token, HttpMethod.Post,
            "/api/analytics/v1/stocks-report/" + (fbs ? "seller-warehouses" : "wb-warehouses"),
            new { nmIds = Array.Empty<long>(), limit = 250000, offset }, ct);
    public Task<JsonDocument> BudgetsAsync(string token, long[] ids, CancellationToken ct)
    {
        if (ids.Length is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(ids));
        return Read("wb-operations-promotion", token, HttpMethod.Post, "/api/advert/v2/budget", new { advertIds = ids }, ct);
    }
    public Task<JsonDocument> BalanceAsync(string token, CancellationToken ct) =>
        Read("wb-operations-promotion", token, HttpMethod.Get, "/adv/v1/balance", null, ct);
    public Task<JsonDocument> DemandAsync(string token, DateOnly start, DateOnly end, int offset, CancellationToken ct) =>
        Read("wb-operations-analytics", token, HttpMethod.Post, "/api/analytics/v3/sales-funnel/products",
            new { selectedPeriod = new { start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") },
                pastPeriod = new { start = start.AddDays(-14).ToString("yyyy-MM-dd"), end = start.AddDays(-1).ToString("yyyy-MM-dd") },
                nmIds = Array.Empty<long>(), brandNames = Array.Empty<string>(), subjectIds = Array.Empty<long>(), tagIds = Array.Empty<long>(),
                skipDeletedNm = false, limit = 1000, offset }, ct);

    private async Task<JsonDocument> Read(string client, string token, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body);
        using var http = factory.CreateClient(client);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new WbApiException(response.StatusCode, WbPromotionClient.GetRetryAfter(response));
        // 204 explicitly means no data, not proof of zero inventory.
        if (response.StatusCode == HttpStatusCode.NoContent) throw new JsonException("Источник WB не вернул данные.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}

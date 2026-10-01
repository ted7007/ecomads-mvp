using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecomads.WebApplication.Services.Wb;

public interface IWbSalesFunnelClient
{
    Task<JsonDocument> GetGroupedHistoryAsync(string token, DateOnly start, DateOnly end, CancellationToken cancellationToken);
    Task<JsonDocument> GetProductsAsync(string token, DateOnly day, DateOnly pastDay, int offset,
        CancellationToken cancellationToken);
}

public sealed class WbSalesFunnelClient(HttpClient httpClient) : IWbSalesFunnelClient
{
    public async Task<JsonDocument> GetProductsAsync(string token, DateOnly day, DateOnly pastDay, int offset,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || offset % 1000 != 0) throw new ArgumentOutOfRangeException(nameof(offset));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/v3/sales-funnel/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            selectedPeriod = new { start = day.ToString("yyyy-MM-dd"), end = day.ToString("yyyy-MM-dd") },
            pastPeriod = new { start = pastDay.ToString("yyyy-MM-dd"), end = pastDay.ToString("yyyy-MM-dd") },
            nmIds = Array.Empty<long>(), brandNames = Array.Empty<string>(), subjectIds = Array.Empty<long>(),
            tagIds = Array.Empty<long>(), skipDeletedNm = false, limit = 1000, offset
        });
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new WbApiException(response.StatusCode, WbPromotionClient.GetRetryAfter(response));
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new JsonException("Товары воронки WB имеют неожиданный формат.");
        }
        return document;
    }

    public async Task<JsonDocument> GetGroupedHistoryAsync(string token, DateOnly start, DateOnly end,
        CancellationToken cancellationToken)
    {
        if (end < start || end.DayNumber - start.DayNumber > 6)
            throw new ArgumentException("Воронка WB: период не более 7 дней.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/v3/sales-funnel/grouped/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            selectedPeriod = new { start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") },
            brandNames = Array.Empty<string>(), subjectIds = Array.Empty<long>(), tagIds = Array.Empty<long>(),
            skipDeletedNm = false, aggregationLevel = "day"
        });
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new WbApiException(response.StatusCode, WbPromotionClient.GetRetryAfter(response));
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new JsonException("Воронка WB имеет неожиданный формат.");
        }
        return document;
    }
}

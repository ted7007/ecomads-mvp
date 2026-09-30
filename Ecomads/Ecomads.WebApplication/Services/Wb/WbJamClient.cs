using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecomads.WebApplication.Services.Wb;

public interface IWbJamClient
{
    Task<JsonDocument> GetSearchTextsAsync(string token, IReadOnlyList<long> nmIds,
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
}

public sealed class WbJamClient(HttpClient httpClient) : IWbJamClient
{
    public async Task<JsonDocument> GetSearchTextsAsync(string token, IReadOnlyList<long> nmIds,
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        if (nmIds.Count is < 1 or > 50 || endDate < startDate || endDate.DayNumber - startDate.DayNumber > 6)
            throw new ArgumentException("Отчёт Джема: от 1 до 50 товаров и не более 7 дней.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/search-report/product/search-texts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            currentPeriod = new { start = startDate.ToString("yyyy-MM-dd"), end = endDate.ToString("yyyy-MM-dd") },
            nmIds,
            topOrderBy = "orders",
            includeSubstitutedSKUs = false,
            includeSearchTexts = true,
            orderBy = new { field = "avgPosition", mode = "asc" },
            limit = 30
        });
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new WbApiException(response.StatusCode, WbPromotionClient.GetRetryAfter(response));
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new JsonException("Отчёт Джема WB имеет неожиданный формат.");
        }
        return document;
    }
}

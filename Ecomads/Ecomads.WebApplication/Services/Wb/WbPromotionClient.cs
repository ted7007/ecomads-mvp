using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public TimeSpan? RetryAfter { get; }

    public WbApiException(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
        : base($"Wildberries API вернул HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}

public sealed record WbCampaignInfo(
    long Id,
    string Name,
    int Status,
    string? PaymentType,
    IReadOnlyList<long> NomenclatureIds,
    DateTime? CreatedAtUtc = null,
    DateTime? StartedAtUtc = null,
    DateTime? DeletedAtUtc = null,
    DateTime? UpdatedAtUtc = null);

public sealed record WbNormQueryPair(long AdvertId, long NmId);

public interface IWbPromotionClient
{
    Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string token, CancellationToken cancellationToken);
    Task<JsonDocument> GetFullStatsAsync(string token, IReadOnlyList<long> campaignIds,
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
    Task<JsonDocument> GetNormQueryStatsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs,
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
}

public sealed class WbPromotionClient(HttpClient httpClient) : IWbPromotionClient
{
    public async Task<JsonDocument> GetNormQueryStatsAsync(string token, IReadOnlyList<WbNormQueryPair> pairs,
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        if (pairs.Count is < 1 or > 100 || endDate < startDate || endDate.DayNumber - startDate.DayNumber > 6)
        {
            throw new ArgumentException("Статистика кластеров WB: от 1 до 100 пар и не более 7 дней.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/adv/v1/normquery/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            from = startDate.ToString("yyyy-MM-dd"),
            to = endDate.ToString("yyyy-MM-dd"),
            items = pairs.Select(pair => new { advertId = pair.AdvertId, nmId = pair.NmId }).ToArray()
        });
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new WbApiException(response.StatusCode, GetRetryAfter(response));
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new JsonException("Статистика кластеров WB имеет неожиданный формат.");
        }
        return document;
    }

    public async Task<JsonDocument> GetFullStatsAsync(string token, IReadOnlyList<long> campaignIds,
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        if (campaignIds.Count is < 1 or > 50 || endDate < startDate || endDate.DayNumber - startDate.DayNumber > 30)
        {
            throw new ArgumentException("Статистика WB: от 1 до 50 кампаний и не более 31 дня.");
        }

        var ids = string.Join(",", campaignIds);
        var path = $"/adv/v3/fullstats?ids={ids}&beginDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new WbApiException(response.StatusCode, GetRetryAfter(response));
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            var shape = document.RootElement.ValueKind == JsonValueKind.Object
                ? "object fields: " + string.Join(", ", document.RootElement.EnumerateObject()
                    .Select(property => property.Name).Take(12))
                : document.RootElement.ValueKind.ToString();
            document.Dispose();
            throw new JsonException($"Статистика WB имеет неожиданный формат ({shape}).");
        }
        return document;
    }

    public async Task<IReadOnlyList<WbCampaignInfo>> GetCampaignsAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/advert/v2/adverts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new WbApiException(response.StatusCode, GetRetryAfter(response));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("adverts", out var adverts) || adverts.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("В ответе Wildberries отсутствует массив adverts.");
        }

        var result = new List<WbCampaignInfo>(adverts.GetArrayLength());
        foreach (var advert in adverts.EnumerateArray())
        {
            var id = advert.GetProperty("id").GetInt64();
            var status = advert.GetProperty("status").GetInt32();
            var settings = advert.GetProperty("settings");
            var name = settings.GetProperty("name").GetString() ?? id.ToString();
            var paymentType = settings.TryGetProperty("payment_type", out var payment)
                ? payment.GetString()
                : null;
            var nms = new List<long>();
            if (advert.TryGetProperty("nm_settings", out var nmSettings) && nmSettings.ValueKind == JsonValueKind.Array)
            {
                foreach (var nm in nmSettings.EnumerateArray())
                {
                    if (nm.TryGetProperty("nm_id", out var nmId) && nmId.TryGetInt64(out var value))
                    {
                        nms.Add(value);
                    }
                }
            }
            DateTime? Timestamp(string property) => advert.TryGetProperty("timestamps", out var timestamps) &&
                timestamps.ValueKind == JsonValueKind.Object && timestamps.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed)
                    ? parsed.UtcDateTime : null;
            result.Add(new WbCampaignInfo(id, name, status, paymentType, nms,
                Timestamp("created"), Timestamp("started"), Timestamp("deleted"), Timestamp("updated")));
        }

        return result;
    }

    internal static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        TimeSpan? custom = null;
        if (response.Headers.TryGetValues("X-Ratelimit-Retry", out var values) &&
            double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
            custom = TimeSpan.FromSeconds(seconds);
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return custom > delta ? custom : delta;
        if (header?.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return custom > remaining ? custom : remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        return custom;
    }
}

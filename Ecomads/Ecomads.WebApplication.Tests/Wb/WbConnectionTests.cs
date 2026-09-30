using System.Net;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbConnectionTests
{
    [Fact]
    public void TokenClaims_RejectsExpiredAndTestTokens()
    {
        var service = new WbTokenService(DataProtectionProvider.Create("wb-token-tests"));
        Assert.Throws<ArgumentException>(() => service.ReadClaims(CreateToken(DateTimeOffset.UtcNow.AddMinutes(-1), false)));
        Assert.Throws<ArgumentException>(() => service.ReadClaims(CreateToken(DateTimeOffset.UtcNow.AddDays(1), true)));
        Assert.Throws<ArgumentException>(() => service.ReadClaims(CreateToken(DateTimeOffset.UtcNow.AddDays(1), false, 0)));
    }

    [Fact]
    public void TokenProtection_RoundTripsWithoutPlaintext()
    {
        var service = new WbTokenService(DataProtectionProvider.Create("wb-token-tests"));
        var token = CreateToken(DateTimeOffset.UtcNow.AddDays(1), false);
        var claims = service.ReadClaims(token);
        var protectedToken = service.Protect(token);

        Assert.Equal("9c79b5c9-0799-4739-a908-81e4280a6810", claims.SellerId);
        Assert.DoesNotContain(token, protectedToken);
        Assert.Equal(token, service.Unprotect(protectedToken));
    }

    [Fact]
    public async Task CampaignClient_ParsesObservedResponseAndDoesNotReuseAuthorizationHeader()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://advert-api.wildberries.ru") };
        var client = new WbPromotionClient(http);

        var campaigns = await client.GetCampaignsAsync("secret-token", CancellationToken.None);

        Assert.Single(campaigns);
        Assert.Equal(35174765, campaigns[0].Id);
        Assert.Equal("cpm", campaigns[0].PaymentType);
        Assert.Equal(new long[] { 123, 456 }, campaigns[0].NomenclatureIds);
        Assert.Equal("Bearer secret-token", handler.LastAuthorization);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task CampaignClient_PropagatesRateLimitWithoutExposingBody()
    {
        using var http = new HttpClient(new RecordingHandler(HttpStatusCode.TooManyRequests))
        {
            BaseAddress = new Uri("https://advert-api.wildberries.ru")
        };
        var exception = await Assert.ThrowsAsync<WbApiException>(() =>
            new WbPromotionClient(http).GetCampaignsAsync("secret-token", CancellationToken.None));
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.DoesNotContain("secret-token", exception.Message);
    }

    [Fact]
    public async Task FullStatsClient_UsesCampaignBatchAndReadsRetryAfterDate()
    {
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(40);
        var handler = new RecordingHandler(HttpStatusCode.TooManyRequests, retryAt);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://advert-api.wildberries.ru") };

        var exception = await Assert.ThrowsAsync<WbApiException>(() =>
            new WbPromotionClient(http).GetFullStatsAsync("secret-token", [35174765, 35736322],
                new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7), CancellationToken.None));

        Assert.Contains("ids=35174765,35736322", handler.LastUri);
        Assert.Contains("beginDate=2026-07-01&endDate=2026-07-07", handler.LastUri);
        Assert.InRange(exception.RetryAfter!.Value.TotalMinutes, 39, 41);
    }

    [Fact]
    public async Task NormQueryClient_SendsObservedPairContract()
    {
        var handler = new RecordingHandler(responseBody: """{"items":[]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://advert-api.wildberries.ru") };

        using var response = await new WbPromotionClient(http).GetNormQueryStatsAsync("secret-token",
            [new WbNormQueryPair(35174765, 123)], new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 7), CancellationToken.None);

        Assert.Equal("https://advert-api.wildberries.ru/adv/v1/normquery/stats", handler.LastUri);
        using var request = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("2026-07-01", request.RootElement.GetProperty("from").GetString());
        Assert.Equal(35174765, request.RootElement.GetProperty("items")[0].GetProperty("advertId").GetInt64());
        Assert.Equal(123, request.RootElement.GetProperty("items")[0].GetProperty("nmId").GetInt64());
        Assert.Equal(JsonValueKind.Array, response.RootElement.GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task JamClient_UsesSearchReportContractAndDoesNotLeakToken()
    {
        var handler = new RecordingHandler(responseBody: """{"data":{"items":[],"currency":"RUB"}}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru") };
        using var response = await new WbJamClient(http).GetSearchTextsAsync("secret-token", [123],
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7), CancellationToken.None);
        Assert.Equal("https://seller-analytics-api.wildberries.ru/api/v2/search-report/product/search-texts", handler.LastUri);
        Assert.Equal("Bearer secret-token", handler.LastAuthorization);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(123, body.RootElement.GetProperty("nmIds")[0].GetInt64());
        Assert.Equal("2026-07-01", body.RootElement.GetProperty("currentPeriod").GetProperty("start").GetString());
        Assert.Equal("orders", body.RootElement.GetProperty("topOrderBy").GetString());
        Assert.Equal(30, body.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal(JsonValueKind.Array, response.RootElement.GetProperty("data").GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task JamClient_ReportsAccessDeniedWithoutExposingResponseBody()
    {
        using var http = new HttpClient(new RecordingHandler(HttpStatusCode.Forbidden,
            responseBody: "commercial account details"))
        { BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru") };
        var error = await Assert.ThrowsAsync<WbApiException>(() =>
            new WbJamClient(http).GetSearchTextsAsync("secret-token", [123],
                new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7), CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.DoesNotContain("secret-token", error.Message);
        Assert.DoesNotContain("commercial account details", error.Message);
    }

    [Fact]
    public async Task JamClient_RespectsWildberriesRateLimitRetryHeader()
    {
        using var http = new HttpClient(new RecordingHandler(HttpStatusCode.TooManyRequests,
            rateLimitRetrySeconds: 4200))
        { BaseAddress = new Uri("https://seller-analytics-api.wildberries.ru") };
        var error = await Assert.ThrowsAsync<WbApiException>(() =>
            new WbJamClient(http).GetSearchTextsAsync("secret-token", [123],
                new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7), CancellationToken.None));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(4200), error.RetryAfter);
    }

    private static string CreateToken(DateTimeOffset expiry, bool isTest, long scopes = (1L << 6) | (1L << 30))
    {
        static string Encode(object value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode(new { alg = "none" })}.{Encode(new { sid = "9c79b5c9-0799-4739-a908-81e4280a6810", exp = expiry.ToUnixTimeSeconds(), acc = 1, s = scopes, t = isTest })}.signature";
    }

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK,
        DateTimeOffset? retryAt = null, string? responseBody = null,
        int? rateLimitRetrySeconds = null) : HttpMessageHandler
    {
        public string? LastAuthorization { get; private set; }
        public string? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastUri = request.RequestUri?.ToString();
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var body = responseBody ?? (status == HttpStatusCode.OK
                ? """{"adverts":[{"id":35174765,"status":11,"settings":{"name":"Demo","payment_type":"cpm"},"nm_settings":[{"nm_id":123},{"nm_id":456}]}]}"""
                : "internal details");
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (retryAt.HasValue) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAt.Value);
            if (rateLimitRetrySeconds.HasValue)
                response.Headers.TryAddWithoutValidation("X-Ratelimit-Retry", rateLimitRetrySeconds.Value.ToString());
            return response;
        }
    }
}

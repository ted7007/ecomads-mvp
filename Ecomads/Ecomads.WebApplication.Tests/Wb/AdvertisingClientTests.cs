using System.Net;
using System.Text;
using System.Text.Json;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class AdvertisingClientTests
{
    [Theory]
    [InlineData(false, "/adv/v0/normquery/get-bids", "{\"bids\":[]}")]
    [InlineData(true, "/adv/v0/normquery/get-minus", "{\"items\":[]}")]
    public async Task OnlyReadEndpointsUseSnakeCasePairBody(bool minus, string path, string response)
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal(path, request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var pair = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(123, pair.GetProperty("advert_id").GetInt64()); Assert.Equal(456, pair.GetProperty("nm_id").GetInt64());
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        })) { BaseAddress = new("https://advert-api.wildberries.ru") };
        using var result = await new WbPromotionClient(http).GetClusterSettingsAsync("token", [new(123, 456)], minus, default);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }
    [Fact]
    public async Task BadShapeIsNotAnEmptySettingsResponse()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{}") }))) { BaseAddress = new("https://advert-api.wildberries.ru") };
        await Assert.ThrowsAsync<JsonException>(() => new WbPromotionClient(http).GetClusterSettingsAsync("token", [new(123, 456)], false, default));
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request);
    }
}

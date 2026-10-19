using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbJobStateTests
{
    [Fact]
    public void PostgresJsonbFormattingDoesNotChangePageIdentity() => Assert.True(WbJobState.SameJson(
        "[{\"StartDate\":\"2026-10-13\",\"EndDate\":\"2026-10-13\",\"CampaignIds\":[]}]",
        "[{\"EndDate\": \"2026-10-13\", \"StartDate\": \"2026-10-13\", \"CampaignIds\": []}]"));

    [Theory]
    [InlineData(null, "{}")]
    [InlineData("{\"Offset\":0}", "{\"Offset\":1000}")]
    [InlineData("[1,2]", "[2,1]")]
    public void DifferentPageOrRequestBatchIsNotEqual(string? first, string? second) => Assert.False(WbJobState.SameJson(first, second));
}

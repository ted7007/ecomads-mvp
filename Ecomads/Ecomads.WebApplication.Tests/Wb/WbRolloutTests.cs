using Ecomads.WebApplication.Services.Wb;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbRolloutTests
{
    [Fact]
    public void InvalidListDoesNotAccidentallyEnableEveryStore()
    {
        var store = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Wb:Ingestion:StoreIds"] = "invalid" }).Build();
        var rollout = new WbIngestionRollout(config);
        Assert.False(rollout.Allows(store));
        config["Wb:Ingestion:StoreIds"] = $" {store}, {store} ";
        Assert.True(rollout.Allows(store)); Assert.False(rollout.Allows(Guid.NewGuid()));
        config["Wb:Ingestion:StoreIds"] = "";
        Assert.True(rollout.Allows(Guid.NewGuid()));
    }
}

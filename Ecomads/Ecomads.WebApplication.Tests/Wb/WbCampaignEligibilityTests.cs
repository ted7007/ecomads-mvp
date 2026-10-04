using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Xunit;

namespace Ecomads.WebApplication.Tests.Wb;

public sealed class WbCampaignEligibilityTests
{
    private static readonly DateOnly PeriodStart = new(2026, 9, 3);

    [Fact]
    public void FinishedCampaignWithEarlierDeletionIsExcluded()
    {
        var campaign = new Campaign { WbStatus = 7,
            WbDeletedAtUtc = new DateTime(2026, 9, 2, 20, 59, 0, DateTimeKind.Utc) };
        Assert.True(WbCampaignEligibility.FinishedBefore(campaign, PeriodStart));
    }

    [Fact]
    public void SentinelDeletionUsesLastUpdate()
    {
        var campaign = new Campaign { WbStatus = 7,
            WbDeletedAtUtc = new DateTime(2099, 12, 31, 21, 0, 0, DateTimeKind.Utc),
            WbUpdatedAtUtc = new DateTime(2026, 9, 2, 20, 59, 0, DateTimeKind.Utc) };
        Assert.True(WbCampaignEligibility.FinishedBefore(campaign, PeriodStart));
        campaign.WbUpdatedAtUtc = new DateTime(2026, 9, 2, 21, 0, 0, DateTimeKind.Utc);
        Assert.False(WbCampaignEligibility.FinishedBefore(campaign, PeriodStart));
    }

    [Fact]
    public void ActiveCampaignAndRecentDeletionAreKept()
    {
        var campaign = new Campaign { WbStatus = 9,
            WbUpdatedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.False(WbCampaignEligibility.FinishedBefore(campaign, PeriodStart));
        campaign.WbStatus = 7;
        campaign.WbDeletedAtUtc = new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc);
        Assert.False(WbCampaignEligibility.FinishedBefore(campaign, PeriodStart));
    }
}

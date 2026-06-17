using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services;

public interface IUserAccessService
{
    Task<UserAccessStateDto> GetAccessStateAsync(Guid userId);

    Task<bool> HasProductAccessAsync(Guid userId);

    Task<bool> ShouldRequireDemoFeedbackAsync(Guid userId);

    Task GrantMvpAccessAfterFeedbackAsync(Guid userId);
}

public class UserAccessService : IUserAccessService
{
    private readonly EcomadsDbContext _dbContext;
    private readonly ILogger<UserAccessService> _logger;

    public UserAccessService(EcomadsDbContext dbContext, ILogger<UserAccessService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<UserAccessStateDto> GetAccessStateAsync(Guid userId)
    {
        var seller = await _dbContext.Sellers.FirstOrDefaultAsync(x => x.Id == userId);
        if (seller == null)
        {
            return new UserAccessStateDto
            {
                UserId = userId,
                AccessType = UserAccessType.Regular,
                DemoStatus = DemoAccessStatus.None,
                HasProductAccess = false,
                ShouldRequireDemoFeedback = false
            };
        }

        var now = DateTime.UtcNow;
        var demoTimeLeft = seller.DemoExpiresAtUtc.HasValue
            ? seller.DemoExpiresAtUtc.Value - now
            : (TimeSpan?)null;
        var hasProductAccess = HasProductAccess(seller, now);
        var shouldRequireDemoFeedback = ShouldRequireDemoFeedback(seller, now);

        if (shouldRequireDemoFeedback && seller.DemoStatus != DemoAccessStatus.ExpiredAwaitingFeedback)
        {
            seller.DemoStatus = DemoAccessStatus.ExpiredAwaitingFeedback;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Demo access expired for user {UserId}. ExpiresAtUtc: {DemoExpiresAtUtc}",
                seller.Id,
                seller.DemoExpiresAtUtc);
        }

        return new UserAccessStateDto
        {
            UserId = seller.Id,
            IsDemoUser = seller.IsDemoUser,
            AccessType = seller.AccessType,
            DemoStatus = seller.DemoStatus,
            HasProductAccess = hasProductAccess,
            ShouldRequireDemoFeedback = shouldRequireDemoFeedback,
            DemoStartedAtUtc = seller.DemoStartedAtUtc,
            DemoExpiresAtUtc = seller.DemoExpiresAtUtc,
            DemoDaysLeft = GetDemoDaysLeft(demoTimeLeft),
            DemoTimeLeft = GetPositiveTimeLeft(demoTimeLeft)
        };
    }

    public async Task<bool> HasProductAccessAsync(Guid userId)
    {
        var accessState = await GetAccessStateAsync(userId);
        return accessState.HasProductAccess;
    }

    public async Task<bool> ShouldRequireDemoFeedbackAsync(Guid userId)
    {
        var accessState = await GetAccessStateAsync(userId);
        return accessState.ShouldRequireDemoFeedback;
    }

    public async Task GrantMvpAccessAfterFeedbackAsync(Guid userId)
    {
        var seller = await _dbContext.Sellers.FirstOrDefaultAsync(x => x.Id == userId);
        if (seller == null)
        {
            throw new InvalidOperationException($"Seller {userId} was not found.");
        }

        var now = DateTime.UtcNow;
        seller.DemoStatus = DemoAccessStatus.FeedbackSubmitted;
        seller.AccessType = UserAccessType.MvpAccess;
        seller.DemoFeedbackSubmittedAtUtc = now;
        seller.MvpAccessGrantedAtUtc = now;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Demo user {UserId} received MVP access after feedback",
            seller.Id);
    }

    private static bool HasProductAccess(Seller seller, DateTime now)
    {
        return seller.AccessType switch
        {
            UserAccessType.Regular => true,
            UserAccessType.MvpAccess => true,
            UserAccessType.Demo => seller.DemoExpiresAtUtc.HasValue && seller.DemoExpiresAtUtc.Value > now,
            _ => false
        };
    }

    private static bool ShouldRequireDemoFeedback(Seller seller, DateTime now)
    {
        return seller.AccessType == UserAccessType.Demo
            && seller.DemoStatus != DemoAccessStatus.FeedbackSubmitted
            && (!seller.DemoExpiresAtUtc.HasValue || seller.DemoExpiresAtUtc.Value <= now);
    }

    private static int? GetDemoDaysLeft(TimeSpan? demoTimeLeft)
    {
        var positiveTimeLeft = GetPositiveTimeLeft(demoTimeLeft);
        if (!positiveTimeLeft.HasValue)
        {
            return null;
        }

        return Math.Max(0, (int)Math.Ceiling(positiveTimeLeft.Value.TotalDays));
    }

    private static TimeSpan? GetPositiveTimeLeft(TimeSpan? demoTimeLeft)
    {
        if (!demoTimeLeft.HasValue)
        {
            return null;
        }

        return demoTimeLeft.Value > TimeSpan.Zero ? demoTimeLeft.Value : TimeSpan.Zero;
    }
}

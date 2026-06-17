using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Models;

public class UserAccessStateDto
{
    public Guid UserId { get; set; }

    public bool IsDemoUser { get; set; }

    public UserAccessType AccessType { get; set; }

    public DemoAccessStatus DemoStatus { get; set; }

    public bool HasProductAccess { get; set; }

    public bool ShouldRequireDemoFeedback { get; set; }

    public DateTime? DemoStartedAtUtc { get; set; }

    public DateTime? DemoExpiresAtUtc { get; set; }

    public int? DemoDaysLeft { get; set; }

    public TimeSpan? DemoTimeLeft { get; set; }
}

using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Data;

namespace Ecomads.WebApplication.Tests.Integration;

internal static class TestData
{
    // Recommendation fixtures explicitly declare a current, complete source snapshot.
    // A completed queue job by itself no longer certifies data quality.
    public static void AddConfirmedClusterDay(EcomadsDbContext db, Guid store, Guid campaign, Guid article, DateOnly date)
    {
        var now = DateTime.UtcNow;
        db.WbSourceQualities.Add(new() { StoreId = store, Source = "clusters", Entity = $"{campaign}:{article}",
            StartDate = date, EndDate = date, State = "data", LastAttemptAtUtc = now, AcceptedAtUtc = now });
    }

    public static Seller CreateRegularSeller()
    {
        return new Seller
        {
            Id = Guid.NewGuid(),
            Name = "Regular Seller",
            Email = $"regular-{Guid.NewGuid():N}@example.test",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsDemoUser = false,
            AccessType = UserAccessType.Regular,
            DemoStatus = DemoAccessStatus.None
        };
    }

    public static Seller CreateActiveDemoSeller()
    {
        var now = DateTime.UtcNow;

        return new Seller
        {
            Id = Guid.NewGuid(),
            Name = "Demo Seller",
            Email = $"demo-{Guid.NewGuid():N}@example.test",
            PasswordHash = "hash",
            CreatedAt = now,
            IsDemoUser = true,
            AccessType = UserAccessType.Demo,
            DemoStatus = DemoAccessStatus.Active,
            DemoStartedAtUtc = now,
            DemoExpiresAtUtc = now.AddDays(3)
        };
    }

    public static Seller CreateExpiredDemoSeller()
    {
        var now = DateTime.UtcNow;

        return new Seller
        {
            Id = Guid.NewGuid(),
            Name = "Expired Demo Seller",
            Email = $"expired-{Guid.NewGuid():N}@example.test",
            PasswordHash = "hash",
            CreatedAt = now.AddDays(-4),
            IsDemoUser = true,
            AccessType = UserAccessType.Demo,
            DemoStatus = DemoAccessStatus.Active,
            DemoStartedAtUtc = now.AddDays(-4),
            DemoExpiresAtUtc = now.AddDays(-1)
        };
    }
}

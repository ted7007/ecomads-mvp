namespace Ecomads.WebApplication.Utilities;

internal static class UtcDate
{
    public static DateTime FromDateOnly(DateOnly value)
    {
        return value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    public static DateTime FromNullableDateOnly(DateOnly? value, DateTime fallbackUtc)
    {
        return value.HasValue
            ? FromDateOnly(value.Value)
            : EnsureUtcKind(fallbackUtc);
    }

    private static DateTime EnsureUtcKind(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}

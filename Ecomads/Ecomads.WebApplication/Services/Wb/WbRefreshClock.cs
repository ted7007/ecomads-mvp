namespace Ecomads.WebApplication.Services.Wb;

public static class WbRefreshClock
{
    public static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    public static DateTime LatestSlot(DateTime nowUtc, bool daily)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Moscow);
        var slot = local.Date.AddHours(daily || local.Hour < 18 ? 6 : 18).AddMinutes(15);
        if (slot > local) slot = daily ? slot.AddDays(-1) : slot.AddHours(-12);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slot, DateTimeKind.Unspecified), Moscow);
    }

    public static DateTime NextSlot(DateTime nowUtc) => LatestSlot(nowUtc, false).AddHours(12);
}

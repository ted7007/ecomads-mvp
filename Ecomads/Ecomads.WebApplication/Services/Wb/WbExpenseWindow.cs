namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbExpenseWindow(DateOnly TargetStart, DateOnly TargetEnd, DateOnly RequestStart, DateOnly RequestEnd)
{
    public static WbExpenseWindow Create(DateOnly start, DateOnly end, DateTime nowUtc)
    {
        var today = WbDates.MoscowDay(nowUtc);
        if (end < start || end.DayNumber - start.DayNumber > 28 || end > today)
            throw new ArgumentOutOfRangeException(nameof(end), "Целевая часть расходов: не более 29 дней и не позднее текущего дня МСК.");
        return new(start, end, start.AddDays(-1), end < today ? end.AddDays(1) : today);
    }
}

using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbClusterPosition(decimal? AveragePosition, int Days, string Status, string? Reason);

public static class WbClusterPositionCalculator
{
    // WB stores a daily average. Its observation weights are not available here;
    // expose an explicitly labelled arithmetic mean of the available daily values.
    public static WbClusterPosition Calculate(DateOnly? start, DateOnly? end,
        IReadOnlyList<WbClusterStatistic> rows, bool periodComplete, bool currentDay)
    {
        if (!start.HasValue || !end.HasValue || start > end)
            return new(null, 0, "unavailable", "Выберите начало и конец периода.");
        var selected = rows.Where(x => x.Date >= start && x.Date <= end).ToArray();
        var known = selected.Where(x => x.AveragePosition is > 0).ToArray();
        if (known.Length == 0)
            return new(null, 0, "unavailable", "WB не передал положительную дневную позицию за выбранный период.");
        var days = known.GroupBy(x => x.Date).Select(group => group.Average(x => x.AveragePosition!.Value)).ToArray();
        var incomplete = !periodComplete || currentDay || known.Length != selected.Length;
        return new(Math.Round(days.Average(), 2), days.Length, incomplete ? "preliminary" : "complete",
            "Среднее арифметическое известных дневных позиций WB; дни без позиции не считаются нулём. " +
            $"Дней с позицией: {days.Length}." + (incomplete ? " Данные периода подтверждены не полностью или текущий день ещё не завершён." : ""));
    }
}

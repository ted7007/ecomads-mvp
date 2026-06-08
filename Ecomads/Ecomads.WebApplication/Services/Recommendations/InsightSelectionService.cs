using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IInsightSelectionService
{
    IReadOnlyList<RecommendationInsight> SelectForLlm(IReadOnlyCollection<RecommendationInsight> insights);
}

public sealed class InsightSelectionService : IInsightSelectionService
{
    private readonly RecommendationEngineOptions _options;

    public InsightSelectionService(IOptions<RecommendationEngineOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<RecommendationInsight> SelectForLlm(IReadOnlyCollection<RecommendationInsight> insights)
    {
        ArgumentNullException.ThrowIfNull(insights);

        var selected = new List<RecommendationInsight>();
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        var sortedInsights = SortInsights(insights).ToList();

        AddTop(
            sortedInsights.Where(insight => insight.PriorityLevel == PriorityLevel.Critical),
            3,
            selected,
            selectedIds);

        AddTop(
            sortedInsights.Where(insight => insight.PriorityLevel == PriorityLevel.High),
            5,
            selected,
            selectedIds);

        AddTop(
            sortedInsights.Where(insight => insight.Type == InsightType.ScaleCandidate),
            5,
            selected,
            selectedIds);

        AddTop(
            sortedInsights.Where(insight => insight.Type == InsightType.WatchCandidate),
            5,
            selected,
            selectedIds);

        AddTop(
            sortedInsights.Where(insight => insight.Type == InsightType.LowData),
            5,
            selected,
            selectedIds);

        if (selected.Count < _options.MaxInsightsForLlm)
        {
            AddTop(
                sortedInsights,
                _options.MaxInsightsForLlm - selected.Count,
                selected,
                selectedIds);
        }

        return selected.Take(_options.MaxInsightsForLlm).ToList();
    }

    private static IEnumerable<RecommendationInsight> SortInsights(IEnumerable<RecommendationInsight> insights)
    {
        return insights
            .OrderByDescending(insight => insight.PriorityScore)
            .ThenByDescending(insight => insight.PriorityLevel)
            .ThenBy(insight => insight.Type)
            .ThenBy(insight => insight.EntityType)
            .ThenBy(insight => insight.EntityName, StringComparer.Ordinal)
            .ThenBy(insight => insight.Id, StringComparer.Ordinal);
    }

    private static void AddTop(
        IEnumerable<RecommendationInsight> source,
        int count,
        List<RecommendationInsight> selected,
        HashSet<string> selectedIds)
    {
        if (count <= 0)
        {
            return;
        }

        var added = 0;
        foreach (var insight in source)
        {
            if (selectedIds.Add(insight.Id))
            {
                selected.Add(insight);
                added++;
            }

            if (added >= count)
            {
                break;
            }
        }
    }
}

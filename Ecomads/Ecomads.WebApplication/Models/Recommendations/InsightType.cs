namespace Ecomads.WebApplication.Models.Recommendations;

public enum InsightType
{
    BadSpendWithoutOrders,
    BadDrr,
    ScaleCandidate,
    WatchCandidate,
    LowData,
    IrrelevantButConverting,
    SemanticIrrelevant,
    StockRisk,
    SeasonRisk,
    PositionGrowthCandidate,
    GoodKeyword,
    CampaignEfficiencyProblem,
    CampaignGrowthOpportunity
}

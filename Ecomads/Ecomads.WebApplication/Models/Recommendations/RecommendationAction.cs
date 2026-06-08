namespace Ecomads.WebApplication.Models.Recommendations;

public enum RecommendationAction
{
    Watch,
    CollectMoreData,
    DecreaseBid,
    DecreaseBidCarefully,
    IncreaseBid,
    IncreaseBidGradually,
    IncreaseBidAggressively,
    ConsiderMinusKeyword,
    MinusKeyword,
    ImmediateMinusKeyword,
    MoveToWatchlist,
    Optimize,
    Scale,
    AggressiveScale,
    FindSimilarKeywords,
    Maintain,
    Disable,
    ImmediateDisable,
    AggressiveBidChange,
    SeparateControl,
    ScaleGoodKeywords,
    IncreaseBidForScaleCandidates,
    ExpandRelevantKeywords,
    AcceptHigherDrrTemporarily,
    AggressivelyReduceAllSpend,
    DisableConvertingKeywords
}

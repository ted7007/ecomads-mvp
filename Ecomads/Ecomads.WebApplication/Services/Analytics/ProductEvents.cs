namespace Ecomads.WebApplication.Services.Analytics;

public static class ProductEvents
{
    public const string StatisticsUploaded = "statistics_uploaded";
    public const string DashboardViewed = "dashboard_viewed";
    public const string KeywordRecommendationOpened = "keyword_recommendation_opened";
    public const string ExpectedEffectPageViewed = "expected_effect_page_viewed";
    public const string DemoFeedbackViewed = "demo_feedback_viewed";
    public const string DemoFeedbackSubmitted = "demo_feedback_submitted";

    public const string LlmRecommendationRequested = "llm_recommendation_requested";
    public const string LlmRecommendationGenerated = "llm_recommendation_generated";
    public const string LlmRecommendationFailed = "llm_recommendation_failed";
}

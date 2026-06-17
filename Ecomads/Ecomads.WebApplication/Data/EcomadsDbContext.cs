using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Data;

public class EcomadsDbContext : DbContext
{
    public EcomadsDbContext(DbContextOptions<EcomadsDbContext> options) : base(options) { }

    public DbSet<Seller> Sellers { get; set; }
    public DbSet<Store> Stores { get; set; }
    public DbSet<Compaign> Compaigns { get; set; }
    public DbSet<CompaignStatistics> CompaignStatistics { get; set; }
    public DbSet<KeywordStatistics> KeywordStatistics { get; set; }
    public DbSet<Recommendation> Recommendations { get; set; }
    public DbSet<RecommendationInsightEntity> RecommendationInsights { get; set; }
    public DbSet<DemoFeedback> DemoFeedbacks { get; set; }
    public DbSet<ProductUsageEvent> ProductUsageEvents { get; set; }
    public DbSet<LlmUsageEvent> LlmUsageEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Seller>(entity =>
        {
            entity.ToTable("sellers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255).HasColumnName("email");
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255).HasColumnName("password_hash");
            entity.Property(e => e.Phone).HasMaxLength(50).HasColumnName("phone");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.IsDemoUser).HasColumnName("is_demo_user").HasDefaultValue(false);
            entity.Property(e => e.AccessType).HasColumnName("access_type").HasDefaultValue(UserAccessType.Regular);
            entity.Property(e => e.DemoStatus).HasColumnName("demo_status").HasDefaultValue(DemoAccessStatus.None);
            entity.Property(e => e.DemoStartedAtUtc).HasColumnName("demo_started_at_utc");
            entity.Property(e => e.DemoExpiresAtUtc).HasColumnName("demo_expires_at_utc");
            entity.Property(e => e.DemoFeedbackSubmittedAtUtc).HasColumnName("demo_feedback_submitted_at_utc");
            entity.Property(e => e.MvpAccessGrantedAtUtc).HasColumnName("mvp_access_granted_at_utc");
            
            // Email должен быть уникальным
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<DemoFeedback>(entity =>
        {
            entity.ToTable("demo_feedbacks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.GeneralComment).IsRequired().HasColumnName("general_comment").HasColumnType("text");
            entity.Property(e => e.DashboardClarityScore).HasColumnName("dashboard_clarity_score");
            entity.Property(e => e.RecommendationsUsefulnessScore).HasColumnName("recommendations_usefulness_score");
            entity.Property(e => e.WrongOrQuestionableRecommendations).HasColumnName("wrong_or_questionable_recommendations").HasColumnType("text");
            entity.Property(e => e.MostUsefulFeature).IsRequired().HasColumnName("most_useful_feature").HasMaxLength(80);
            entity.Property(e => e.MissingForRegularUsage).HasColumnName("missing_for_regular_usage").HasColumnType("text");
            entity.Property(e => e.ContinueTestingAnswer).IsRequired().HasColumnName("continue_testing_answer").HasMaxLength(40);
            entity.Property(e => e.WillingToPayAnswer).IsRequired().HasColumnName("willing_to_pay_answer").HasMaxLength(40);
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne<Seller>()
                .WithOne()
                .HasForeignKey<DemoFeedback>(e => e.UserId)
                .HasConstraintName("FK_demo_feedbacks_sellers_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId).IsUnique();
        });

        modelBuilder.Entity<ProductUsageEvent>(entity =>
        {
            entity.ToTable("product_usage_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.EventName).IsRequired().HasColumnName("event_name").HasMaxLength(120);
            entity.Property(e => e.FeatureName).IsRequired().HasColumnName("feature_name").HasMaxLength(120);
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.KeywordId).HasColumnName("keyword_id");
            entity.Property(e => e.LlmUsageId).HasColumnName("llm_usage_id");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            entity.Property(e => e.Path).HasColumnName("path").HasMaxLength(500);
            entity.Property(e => e.Method).HasColumnName("method").HasMaxLength(20);
            entity.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
            entity.Property(e => e.IpHash).HasColumnName("ip_hash").HasMaxLength(128);
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne<LlmUsageEvent>()
                .WithMany()
                .HasForeignKey(e => e.LlmUsageId)
                .HasConstraintName("FK_product_usage_events_llm_usage_events_llm_usage_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.FeatureName);
            entity.HasIndex(e => e.EventName);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.LlmUsageId);
            entity.HasIndex(e => e.CampaignId);
        });

        modelBuilder.Entity<LlmUsageEvent>(entity =>
        {
            entity.ToTable("llm_usage_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.KeywordId).HasColumnName("keyword_id");
            entity.Property(e => e.Provider).IsRequired().HasColumnName("provider").HasMaxLength(80);
            entity.Property(e => e.Model).IsRequired().HasColumnName("model").HasMaxLength(120);
            entity.Property(e => e.OperationName).IsRequired().HasColumnName("operation_name").HasMaxLength(160);
            entity.Property(e => e.PromptTokens).HasColumnName("prompt_tokens");
            entity.Property(e => e.CompletionTokens).HasColumnName("completion_tokens");
            entity.Property(e => e.TotalTokens).HasColumnName("total_tokens");
            entity.Property(e => e.BothubCaps).HasColumnName("bothub_caps").HasColumnType("decimal(18,6)");
            entity.Property(e => e.EstimatedCostRub).HasColumnName("estimated_cost_rub").HasColumnType("decimal(18,6)");
            entity.Property(e => e.IsSuccess).HasColumnName("is_success");
            entity.Property(e => e.HttpStatusCode).HasColumnName("http_status_code");
            entity.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(120);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.RequestMetadataJson).HasColumnName("request_metadata_json").HasColumnType("jsonb");
            entity.Property(e => e.ResponseMetadataJson).HasColumnName("response_metadata_json").HasColumnType("jsonb");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CampaignId);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => e.Model);
            entity.HasIndex(e => e.OperationName);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.IsSuccess);
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("stores");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Description).HasMaxLength(255).HasColumnName("description");
            entity.Property(e => e.Marketplace).HasMaxLength(50).HasColumnName("marketplace").HasDefaultValue("Wildberries");
            entity.Property(e => e.ExternalId).HasMaxLength(100).HasColumnName("external_id");
            entity.Property(e => e.ApiKey).HasMaxLength(500).HasColumnName("api_key");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at");
            entity.Property(e => e.SellerId).HasColumnName("seller_id");
            
            entity.HasOne(e => e.Seller)
                .WithMany(s => s.Stores)
                .HasForeignKey(e => e.SellerId)
                .HasConstraintName("FK_stores_sellers_seller_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Compaign>(entity =>
        {
            entity.ToTable("compaigns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Number).IsRequired().HasMaxLength(100).HasColumnName("number");
            entity.Property(e => e.Description).HasMaxLength(255).HasColumnName("description");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Budget).HasColumnName("budget").HasColumnType("decimal(18,2)");
            entity.Property(e => e.StoreId).HasColumnName("store_id");
            
            entity.HasOne(e => e.Store)
                .WithMany(s => s.Compaigns)
                .HasForeignKey(e => e.StoreId)
                .HasConstraintName("FK_compaigns_stores_store_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompaignStatistics>(entity =>
        {
            entity.ToTable("compaign_statistics");
            entity.HasKey(e => new { e.CompaignId, e.StartDate, e.EndDate, e.Type });
            entity.Property(e => e.CompaignId).HasColumnName("compaign_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.Revenue).HasColumnName("revenue");
            entity.Property(e => e.Spend).HasColumnName("spend");
            entity.Property(e => e.Clicks).HasColumnName("clicks");
            entity.Property(e => e.Ctr).HasColumnName("ctr");
            entity.Property(e => e.Drr).HasColumnName("drr");
            entity.HasOne<Compaign>().WithMany().HasForeignKey(e => e.CompaignId);
        });

        modelBuilder.Entity<KeywordStatistics>(entity =>
        {
            entity.ToTable("keyword_statistics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompaignId).HasColumnName("compaign_id");
            entity.Property(e => e.Phrase).HasColumnName("phrase").HasMaxLength(500);
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Frequency).HasColumnName("frequency");
            entity.Property(e => e.Cpm).HasColumnName("cpm");
            entity.Property(e => e.AvgPosition).HasColumnName("avg_position");
            entity.Property(e => e.Impressions).HasColumnName("impressions");
            entity.Property(e => e.Clicks).HasColumnName("clicks");
            entity.Property(e => e.Ctr).HasColumnName("ctr");
            entity.Property(e => e.Spend).HasColumnName("spend");
            entity.Property(e => e.Orders).HasColumnName("orders");
            entity.Property(e => e.Revenue).HasColumnName("revenue");
            entity.Property(e => e.Drr).HasColumnName("drr");
            entity.HasOne(e => e.Compaign).WithMany().HasForeignKey(e => e.CompaignId).HasConstraintName("FK_keyword_statistics_compaigns_compaign_id");
        });

        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.ToTable("recommendations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.Goal).HasColumnName("goal").HasMaxLength(255);
            entity.Property(e => e.Prompt).HasColumnName("prompt").HasColumnType("text");
            entity.Property(e => e.FullResponse).HasColumnName("full_response").HasColumnType("text");
            entity.Property(e => e.Problem).HasColumnName("problem").HasColumnType("text").HasDefaultValue("");
            entity.Property(e => e.RecommendationText).HasColumnName("recommendation_text").HasColumnType("text").HasDefaultValue("");
            entity.Property(e => e.ExpectedEffect).HasColumnName("expected_effect").HasColumnType("text").HasDefaultValue("");
            entity.Property(e => e.AdditionalData).HasColumnName("additional_data").HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(e => e.RequestMetadata).HasColumnName("request_metadata").HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(50).HasDefaultValue("новая");
            entity.Property(e => e.StatusUpdatedAt).HasColumnName("status_updated_at");
            entity.Property(e => e.UserComment).HasColumnName("user_comment").HasColumnType("text").HasDefaultValue("");
            
            entity.HasOne(e => e.Campaign)
                .WithMany()
                .HasForeignKey(e => e.CampaignId)
                .HasConstraintName("FK_recommendations_compaigns_campaign_id");
        });

        modelBuilder.Entity<RecommendationInsightEntity>(entity =>
        {
            entity.ToTable("recommendation_insights");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(255);
            entity.Property(e => e.RecommendationRunId).HasColumnName("recommendation_run_id");
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.PeriodFrom).HasColumnName("period_from");
            entity.Property(e => e.PeriodTo).HasColumnName("period_to");
            entity.Property(e => e.EntityType).HasColumnName("entity_type").HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.EntityName).HasColumnName("entity_name").HasMaxLength(500);
            entity.Property(e => e.InsightType).HasColumnName("insight_type").HasConversion<string>().HasMaxLength(80);
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.PriorityScore).HasColumnName("priority_score");
            entity.Property(e => e.PriorityLevel).HasColumnName("priority_level").HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ConfidenceLevel).HasColumnName("confidence_level").HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.RecommendedAction).HasColumnName("recommended_action").HasConversion<string>().HasMaxLength(80);
            entity.Property(e => e.DecisionStatus).HasColumnName("decision_status").HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.UserComment).HasColumnName("user_comment").HasColumnType("text");
            entity.Property(e => e.ExpectedEffectType).HasColumnName("expected_effect_type").HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ExpectedEffectMoney).HasColumnName("expected_effect_money").HasColumnType("decimal(18,2)");
            entity.Property(e => e.ExpectedEffectText).HasColumnName("expected_effect_text").HasColumnType("text");
            entity.Property(e => e.ActualEffectMoney).HasColumnName("actual_effect_money").HasColumnType("decimal(18,2)");
            entity.Property(e => e.ActualEffectStatus).HasColumnName("actual_effect_status").HasMaxLength(80);
            entity.Property(e => e.MetricsJson).HasColumnName("metrics").HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(e => e.ReasonCodesJson).HasColumnName("reason_codes").HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(e => e.AllowedActionsJson).HasColumnName("allowed_actions").HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(e => e.ForbiddenActionsJson).HasColumnName("forbidden_actions").HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(e => e.RecommendationRun)
                .WithMany()
                .HasForeignKey(e => e.RecommendationRunId)
                .HasConstraintName("FK_recommendation_insights_recommendations_recommendation_run_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Compaign>()
                .WithMany()
                .HasForeignKey(e => e.CampaignId)
                .HasConstraintName("FK_recommendation_insights_compaigns_campaign_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.RecommendationRunId);
            entity.HasIndex(e => new { e.CampaignId, e.EntityType, e.EntityId });
            entity.HasIndex(e => e.DecisionStatus);
        });
    }
}

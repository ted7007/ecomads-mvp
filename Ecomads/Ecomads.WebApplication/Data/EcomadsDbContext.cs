using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Data;

public class EcomadsDbContext : DbContext
{
    public EcomadsDbContext(DbContextOptions<EcomadsDbContext> options) : base(options) { }

    public DbSet<Seller> Sellers { get; set; }
    public DbSet<Store> Stores { get; set; }
    public DbSet<Campaign> Campaigns { get; set; }
    public DbSet<Nomenclature> Nomenclatures { get; set; }
    public DbSet<CampaignNomenclatureStatistics> CampaignNomenclatureStatistics { get; set; }
    public DbSet<CampaignStatistics> CampaignStatistics { get; set; }
    public DbSet<DemoFeedback> DemoFeedbacks { get; set; }
    public DbSet<WbSyncJob> WbSyncJobs { get; set; }
    public DbSet<WbClusterStatistic> WbClusterStatistics { get; set; }
    public DbSet<WbStoreNorms> WbStoreNorms { get; set; }
    public DbSet<WbCampaignNorms> WbCampaignNorms { get; set; }
    public DbSet<WbNormRevision> WbNormRevisions { get; set; }
    public DbSet<TelegramLinkCode> TelegramLinkCodes { get; set; }
    public DbSet<TelegramChat> TelegramChats { get; set; }
    public DbSet<TelegramDelivery> TelegramDeliveries { get; set; }
    public DbSet<TelegramBotState> TelegramBotStates { get; set; }

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
            entity.Property(e => e.AnswersJson).IsRequired().HasColumnName("answers_json").HasColumnType("jsonb");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne<Seller>()
                .WithOne()
                .HasForeignKey<DemoFeedback>(e => e.UserId)
                .HasConstraintName("FK_demo_feedbacks_sellers_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId).IsUnique();
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
            entity.Property(e => e.ApiKey).HasColumnType("text").HasColumnName("api_key");
            entity.Property(e => e.TokenLastFour).HasMaxLength(4).HasColumnName("token_last_four");
            entity.Property(e => e.TokenExpiresAtUtc).HasColumnName("token_expires_at_utc");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at");
            entity.Property(e => e.SellerId).HasColumnName("seller_id");
            entity.HasIndex(e => new { e.Marketplace, e.ExternalId })
                .IsUnique()
                .HasFilter("external_id IS NOT NULL");
            
            entity.HasOne(e => e.Seller)
                .WithMany(s => s.Stores)
                .HasForeignKey(e => e.SellerId)
                .HasConstraintName("FK_stores_sellers_seller_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.ToTable("campaigns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.WbCampaignId).IsRequired().HasMaxLength(50).HasColumnName("wb_campaign_id");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.WbStatus).HasColumnName("wb_status");
            entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.StoreId).HasColumnName("store_id");
            entity.HasOne(e => e.Store)
                .WithMany(s => s.Campaigns)
                .HasForeignKey(e => e.StoreId)
                .HasConstraintName("FK_campaigns_stores_store_id")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.StoreId, e.WbCampaignId }).IsUnique();
        });

        modelBuilder.Entity<WbSyncJob>(entity =>
        {
            entity.ToTable("wb_sync_jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.StoreId).HasColumnName("store_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.CampaignIdsJson).HasColumnName("campaign_ids_json").HasColumnType("jsonb");
            entity.Property(e => e.PairIdsJson).HasColumnName("pair_ids_json").HasColumnType("jsonb");
            entity.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(32).HasDefaultValue("fullstats");
            entity.Property(e => e.NextCampaignOffset).HasColumnName("next_campaign_offset");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
            entity.Property(e => e.LastRequestAtUtc).HasColumnName("last_request_at_utc");
            entity.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(80);
            entity.HasIndex(e => new { e.StoreId, e.Status, e.NextAttemptAtUtc });
            entity.HasOne<Store>().WithMany().HasForeignKey(e => e.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WbClusterStatistic>(entity =>
        {
            entity.ToTable("wb_cluster_statistics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.NomenclatureId).HasColumnName("nomenclature_id");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.ClusterName).HasColumnName("cluster_name").HasMaxLength(500);
            entity.Property(e => e.Spend).HasColumnName("spend").HasColumnType("decimal(18,2)");
            entity.Property(e => e.Views).HasColumnName("views");
            entity.Property(e => e.Clicks).HasColumnName("clicks");
            entity.Property(e => e.Carts).HasColumnName("carts");
            entity.Property(e => e.Orders).HasColumnName("orders");
            entity.Property(e => e.OrderedProducts).HasColumnName("ordered_products");
            entity.Property(e => e.AveragePosition).HasColumnName("average_position").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Cpc).HasColumnName("cpc").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Cpm).HasColumnName("cpm").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Ctr).HasColumnName("ctr").HasColumnType("decimal(18,4)");
            entity.HasOne<Campaign>().WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Nomenclature>().WithMany().HasForeignKey(e => e.NomenclatureId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CampaignId, e.NomenclatureId, e.Date, e.ClusterName }).IsUnique();
        });

        modelBuilder.Entity<WbStoreNorms>(entity =>
        {
            entity.ToTable("wb_store_norms");
            entity.HasKey(x => x.StoreId);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.TargetDrr).HasColumnName("target_drr").HasColumnType("decimal(8,2)");
            entity.Property(x => x.MinClicks).HasColumnName("min_clicks");
            entity.Property(x => x.MinSpend).HasColumnName("min_spend").HasColumnType("decimal(18,2)");
            entity.Property(x => x.MinOrders).HasColumnName("min_orders");
            entity.Property(x => x.DeviationPercent).HasColumnName("deviation_percent").HasColumnType("decimal(8,2)");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasOne<Store>().WithOne().HasForeignKey<WbStoreNorms>(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WbCampaignNorms>(entity =>
        {
            entity.ToTable("wb_campaign_norms");
            entity.HasKey(x => x.CampaignId);
            entity.Property(x => x.CampaignId).HasColumnName("campaign_id");
            entity.Property(x => x.CustomName).HasColumnName("custom_name").HasMaxLength(255);
            entity.Property(x => x.Goal).HasColumnName("goal").HasMaxLength(255);
            entity.Property(x => x.TargetDrr).HasColumnName("target_drr").HasColumnType("decimal(8,2)");
            entity.Property(x => x.MinClicks).HasColumnName("min_clicks");
            entity.Property(x => x.MinSpend).HasColumnName("min_spend").HasColumnType("decimal(18,2)");
            entity.Property(x => x.MinOrders).HasColumnName("min_orders");
            entity.Property(x => x.DeviationPercent).HasColumnName("deviation_percent").HasColumnType("decimal(8,2)");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasOne<Campaign>().WithOne().HasForeignKey<WbCampaignNorms>(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WbNormRevision>(entity =>
        {
            entity.ToTable("wb_norm_revisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.CampaignId).HasColumnName("campaign_id");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.SettingsJson).HasColumnName("settings_json").HasColumnType("jsonb");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.StoreId, x.CampaignId, x.Version });
        });

        modelBuilder.Entity<TelegramLinkCode>(entity =>
        {
            entity.ToTable("telegram_link_codes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SellerId).HasColumnName("seller_id");
            entity.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
            entity.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(x => x.UsedAtUtc).HasColumnName("used_at_utc");
            entity.HasIndex(x => x.CodeHash).IsUnique();
            entity.HasOne<Seller>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TelegramChat>(entity =>
        {
            entity.ToTable("telegram_chats");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SellerId).HasColumnName("seller_id");
            entity.Property(x => x.ChatId).HasColumnName("chat_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(255);
            entity.Property(x => x.LinkedAtUtc).HasColumnName("linked_at_utc");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.HasIndex(x => x.ChatId).IsUnique();
            entity.HasOne<Seller>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TelegramDelivery>(entity =>
        {
            entity.ToTable("telegram_deliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.ChatId).HasColumnName("chat_id");
            entity.Property(x => x.ReportDate).HasColumnName("report_date");
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(80);
            entity.HasIndex(x => new { x.StoreId, x.ChatId, x.ReportDate }).IsUnique();
            entity.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TelegramChat>().WithMany().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TelegramBotState>(entity =>
        {
            entity.ToTable("telegram_bot_state");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.NextUpdateId).HasColumnName("next_update_id");
        });

        modelBuilder.Entity<Nomenclature>(entity =>
        {
            entity.ToTable("nomenclatures");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WbNomenclatureId).IsRequired().HasMaxLength(50).HasColumnName("wb_nomenclature_id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(1000).HasColumnName("name");
            entity.Property(e => e.StoreId).HasColumnName("store_id");
            entity.HasOne(e => e.Store)
                .WithMany()
                .HasForeignKey(e => e.StoreId)
                .HasConstraintName("FK_nomenclatures_stores_store_id")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.StoreId, e.WbNomenclatureId }).IsUnique();
        });

        modelBuilder.Entity<CampaignNomenclatureStatistics>(entity =>
        {
            entity.ToTable("campaign_nomenclature_statistics");
            entity.HasKey(e => new { e.CampaignId, e.NomenclatureId, e.Date });
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.NomenclatureId).HasColumnName("nomenclature_id");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.Spend).HasColumnName("spend").HasColumnType("decimal(18,2)");
            entity.Property(e => e.Revenue).HasColumnName("revenue").HasColumnType("decimal(18,2)");
            entity.Property(e => e.Impressions).HasColumnName("impressions");
            entity.Property(e => e.Clicks).HasColumnName("clicks");
            entity.Property(e => e.Carts).HasColumnName("carts");
            entity.Property(e => e.Orders).HasColumnName("orders");
            entity.Property(e => e.Cancellations).HasColumnName("cancellations");
            entity.Property(e => e.Ctr).HasColumnName("ctr").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Cr).HasColumnName("cr").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Cpm).HasColumnName("cpm").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Cpc).HasColumnName("cpc").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Cpo).HasColumnName("cpo").HasColumnType("decimal(18,4)");
            entity.Property(e => e.AveragePosition).HasColumnName("average_position").HasColumnType("decimal(18,4)");
            entity.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Nomenclature).WithMany().HasForeignKey(e => e.NomenclatureId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.NomenclatureId, e.Date });
        });

        modelBuilder.Entity<CampaignStatistics>(entity =>
        {
            entity.ToTable("campaign_statistics");
            entity.HasKey(e => new { e.CampaignId, e.Date });
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.Revenue).HasColumnName("revenue").HasColumnType("decimal(18,2)");
            entity.Property(e => e.Spend).HasColumnName("spend").HasColumnType("decimal(18,2)");
            entity.Property(e => e.Clicks).HasColumnName("clicks");
            entity.Property(e => e.Ctr).HasColumnName("ctr").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Drr).HasColumnName("drr").HasColumnType("decimal(18,4)");
            entity.Property(e => e.Impressions).HasColumnName("impressions");
            entity.Property(e => e.Carts).HasColumnName("carts");
            entity.Property(e => e.Orders).HasColumnName("orders");
            entity.Property(e => e.Cancellations).HasColumnName("cancellations");
            entity.HasOne<Campaign>().WithMany().HasForeignKey(e => e.CampaignId);
        });



    }
}

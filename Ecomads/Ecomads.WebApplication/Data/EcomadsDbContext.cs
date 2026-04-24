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
            
            // Email должен быть уникальным
            entity.HasIndex(e => e.Email).IsUnique();
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
    }
}

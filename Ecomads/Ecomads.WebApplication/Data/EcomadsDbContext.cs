using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Data.Models;

namespace Ecomads.WebApplication.Data;

public class EcomadsDbContext : DbContext
{
    public EcomadsDbContext(DbContextOptions<EcomadsDbContext> options) : base(options) { }

    public DbSet<Seller> Sellers { get; set; }
    public DbSet<Compaign> Compaigns { get; set; }
    public DbSet<CompaignStatistics> CompaignStatistics { get; set; }
    public DbSet<KeywordStatistics> KeywordStatistics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Seller>(entity =>
        {
            entity.ToTable("sellers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255).HasColumnName("email");
        });

        modelBuilder.Entity<Compaign>(entity =>
        {
            entity.ToTable("compaigns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Number).IsRequired().HasMaxLength(100).HasColumnName("number");
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
    }
}

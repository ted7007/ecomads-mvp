using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ecomads.WebApplication.Migrations;

/// <summary>
/// Older fullstats imports stored returned campaign days before daily checks existed.
/// An existing row proves that WB returned that day; it says nothing about missing days.
/// </summary>
[DbContext(typeof(EcomadsDbContext))]
[Migration("20261002090000_BackfillExplicitCampaignDailyChecks")]
public sealed class BackfillExplicitCampaignDailyChecks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO wb_campaign_daily_checks
                (campaign_id, date, job_id, checked_at_utc, result, spend)
            SELECT s.campaign_id, s.date::date, NULL, s.date,
                CASE WHEN s.spend = 0 THEN 'zero' ELSE 'data' END, s.spend
            FROM campaign_statistics AS s
            WHERE s.spend >= 0
            ON CONFLICT (campaign_id, date) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM wb_campaign_daily_checks AS c
            USING campaign_statistics AS s
            WHERE c.campaign_id = s.campaign_id
              AND c.date = s.date::date
              AND c.job_id IS NULL
              AND c.checked_at_utc = s.date
              AND c.spend = s.spend;
            """);
    }
}

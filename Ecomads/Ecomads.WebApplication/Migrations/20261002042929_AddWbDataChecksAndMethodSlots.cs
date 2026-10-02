using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbDataChecksAndMethodSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "imported_rows",
                table: "wb_sync_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "items_with_data",
                table: "wb_sync_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "items_without_data",
                table: "wb_sync_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "wb_created_at_utc",
                table: "campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "wb_deleted_at_utc",
                table: "campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "wb_started_at_utc",
                table: "campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "wb_updated_at_utc",
                table: "campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wb_campaign_daily_checks",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    spend = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_campaign_daily_checks", x => new { x.campaign_id, x.date });
                    table.ForeignKey(
                        name: "FK_wb_campaign_daily_checks_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_jam_article_checks",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nomenclature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    has_data = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_jam_article_checks", x => new { x.store_id, x.nomenclature_id, x.start_date, x.end_date });
                    table.ForeignKey(
                        name: "FK_wb_jam_article_checks_nomenclatures_nomenclature_id",
                        column: x => x.nomenclature_id,
                        principalTable: "nomenclatures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_jam_article_checks_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_method_slots",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_request_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_method_slots", x => new { x.store_id, x.method });
                    table.ForeignKey(
                        name: "FK_wb_method_slots_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_jam_article_checks_nomenclature_id",
                table: "wb_jam_article_checks",
                column: "nomenclature_id");

            migrationBuilder.Sql(@"
                INSERT INTO wb_method_slots (store_id, method, last_request_at_utc)
                SELECT store_id,
                    CASE WHEN kind = 'funnel' AND next_campaign_offset > 0 THEN 'funnel_products'
                         WHEN kind IN ('funnel', 'funnel_recent') THEN 'funnel_history'
                         WHEN kind = 'funnel_backfill' THEN 'funnel_products'
                         WHEN kind = 'archive' THEN 'fullstats'
                         ELSE kind END,
                    MAX(last_request_at_utc)
                FROM wb_sync_jobs
                WHERE last_request_at_utc IS NOT NULL
                GROUP BY store_id, CASE WHEN kind = 'funnel' AND next_campaign_offset > 0 THEN 'funnel_products'
                                        WHEN kind IN ('funnel', 'funnel_recent') THEN 'funnel_history'
                                        WHEN kind = 'funnel_backfill' THEN 'funnel_products'
                                        WHEN kind = 'archive' THEN 'fullstats' ELSE kind END
                ON CONFLICT (store_id, method) DO UPDATE
                SET last_request_at_utc = GREATEST(wb_method_slots.last_request_at_utc, EXCLUDED.last_request_at_utc);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_campaign_daily_checks");

            migrationBuilder.DropTable(
                name: "wb_jam_article_checks");

            migrationBuilder.DropTable(
                name: "wb_method_slots");

            migrationBuilder.DropColumn(
                name: "imported_rows",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "items_with_data",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "items_without_data",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "wb_created_at_utc",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "wb_deleted_at_utc",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "wb_started_at_utc",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "wb_updated_at_utc",
                table: "campaigns");
        }
    }
}

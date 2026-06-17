using System;
using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    [DbContext(typeof(EcomadsDbContext))]
    [Migration("20260617143000_AddProductUsageEvents")]
    public partial class AddProductUsageEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_usage_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    feature_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    keyword_id = table.Column<Guid>(type: "uuid", nullable: true),
                    llm_usage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_usage_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_usage_events_campaign_id",
                table: "product_usage_events",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_usage_events_created_at_utc",
                table: "product_usage_events",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_product_usage_events_event_name",
                table: "product_usage_events",
                column: "event_name");

            migrationBuilder.CreateIndex(
                name: "IX_product_usage_events_feature_name",
                table: "product_usage_events",
                column: "feature_name");

            migrationBuilder.CreateIndex(
                name: "IX_product_usage_events_llm_usage_id",
                table: "product_usage_events",
                column: "llm_usage_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_usage_events_user_id",
                table: "product_usage_events",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_usage_events");
        }
    }
}

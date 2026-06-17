using System;
using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    [DbContext(typeof(EcomadsDbContext))]
    [Migration("20260617150000_AddLlmUsageEvents")]
    public partial class AddLlmUsageEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "llm_usage_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    keyword_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    operation_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    total_tokens = table.Column<int>(type: "integer", nullable: true),
                    bothub_caps = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    estimated_cost_rub = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    is_success = table.Column<bool>(type: "boolean", nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    error_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    request_metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    response_metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_usage_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_llm_usage_events_campaign_id",
                table: "llm_usage_events",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_llm_usage_events_created_at_utc",
                table: "llm_usage_events",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_llm_usage_events_is_success",
                table: "llm_usage_events",
                column: "is_success");

            migrationBuilder.CreateIndex(
                name: "IX_llm_usage_events_model",
                table: "llm_usage_events",
                column: "model");

            migrationBuilder.CreateIndex(
                name: "IX_llm_usage_events_operation_name",
                table: "llm_usage_events",
                column: "operation_name");

            migrationBuilder.CreateIndex(
                name: "IX_llm_usage_events_provider",
                table: "llm_usage_events",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "IX_llm_usage_events_user_id",
                table: "llm_usage_events",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_product_usage_events_llm_usage_events_llm_usage_id",
                table: "product_usage_events",
                column: "llm_usage_id",
                principalTable: "llm_usage_events",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_usage_events_llm_usage_events_llm_usage_id",
                table: "product_usage_events");

            migrationBuilder.DropTable(
                name: "llm_usage_events");
        }
    }
}

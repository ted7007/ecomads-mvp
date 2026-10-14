using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CartConversion",
                table: "wb_store_norms",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinCarts",
                table: "wb_store_norms",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "MinViews",
                table: "wb_store_norms",
                type: "integer",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderCartConversion",
                table: "wb_store_norms",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderClickConversion",
                table: "wb_store_norms",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CartConversion",
                table: "wb_campaign_norms",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LaunchDate",
                table: "wb_campaign_norms",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinCarts",
                table: "wb_campaign_norms",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinViews",
                table: "wb_campaign_norms",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderCartConversion",
                table: "wb_campaign_norms",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderClickConversion",
                table: "wb_campaign_norms",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LaunchDate",
                table: "wb_article_norms",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wb_advertising_settings",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    BidType = table.Column<string>(type: "text", nullable: true),
                    PaymentType = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_advertising_settings", x => x.CampaignId);
                    table.ForeignKey(
                        name: "FK_wb_advertising_settings_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_snapshot_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SnoozeUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_snapshot_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_snapshot_decisions_wb_recommendation_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "wb_recommendation_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_snapshot_decisions_RunId_EvaluationId_RuleId_CreatedAtUtc",
                table: "wb_snapshot_decisions",
                columns: new[] { "RunId", "EvaluationId", "RuleId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_advertising_settings");

            migrationBuilder.DropTable(
                name: "wb_snapshot_decisions");

            migrationBuilder.DropColumn(
                name: "CartConversion",
                table: "wb_store_norms");

            migrationBuilder.DropColumn(
                name: "MinCarts",
                table: "wb_store_norms");

            migrationBuilder.DropColumn(
                name: "MinViews",
                table: "wb_store_norms");

            migrationBuilder.DropColumn(
                name: "OrderCartConversion",
                table: "wb_store_norms");

            migrationBuilder.DropColumn(
                name: "OrderClickConversion",
                table: "wb_store_norms");

            migrationBuilder.DropColumn(
                name: "CartConversion",
                table: "wb_campaign_norms");

            migrationBuilder.DropColumn(
                name: "LaunchDate",
                table: "wb_campaign_norms");

            migrationBuilder.DropColumn(
                name: "MinCarts",
                table: "wb_campaign_norms");

            migrationBuilder.DropColumn(
                name: "MinViews",
                table: "wb_campaign_norms");

            migrationBuilder.DropColumn(
                name: "OrderCartConversion",
                table: "wb_campaign_norms");

            migrationBuilder.DropColumn(
                name: "OrderClickConversion",
                table: "wb_campaign_norms");

            migrationBuilder.DropColumn(
                name: "LaunchDate",
                table: "wb_article_norms");
        }
    }
}

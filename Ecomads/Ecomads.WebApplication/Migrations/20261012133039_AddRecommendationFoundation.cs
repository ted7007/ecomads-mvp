using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "nomenclature_id",
                table: "wb_norm_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "recommendation_fields_verified",
                table: "campaign_nomenclature_statistics",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wb_article_norms",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomenclatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetDrr = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_article_norms", x => new { x.CampaignId, x.NomenclatureId });
                    table.ForeignKey(
                        name: "FK_wb_article_norms_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_article_norms_nomenclatures_NomenclatureId",
                        column: x => x.NomenclatureId,
                        principalTable: "nomenclatures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_article_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomenclatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Orders = table.Column<int>(type: "integer", nullable: false),
                    OrderedProducts = table.Column<int>(type: "integer", nullable: true),
                    Revenue = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_article_observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_article_observations_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_article_observations_nomenclatures_NomenclatureId",
                        column: x => x.NomenclatureId,
                        principalTable: "nomenclatures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_recommendation_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_recommendation_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_recommendation_runs_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_recommendation_runs_sellers_SellerId",
                        column: x => x.SellerId,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_recommendation_runs_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_article_norms_NomenclatureId",
                table: "wb_article_norms",
                column: "NomenclatureId");

            migrationBuilder.CreateIndex(
                name: "IX_wb_article_observations_CampaignId_NomenclatureId_Date_Obse~",
                table: "wb_article_observations",
                columns: new[] { "CampaignId", "NomenclatureId", "Date", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_article_observations_NomenclatureId",
                table: "wb_article_observations",
                column: "NomenclatureId");

            migrationBuilder.CreateIndex(
                name: "IX_wb_recommendation_runs_CampaignId_StartDate_EndDate_Fingerp~",
                table: "wb_recommendation_runs",
                columns: new[] { "CampaignId", "StartDate", "EndDate", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wb_recommendation_runs_SellerId",
                table: "wb_recommendation_runs",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_wb_recommendation_runs_StoreId_CalculatedAtUtc",
                table: "wb_recommendation_runs",
                columns: new[] { "StoreId", "CalculatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_article_norms");

            migrationBuilder.DropTable(
                name: "wb_article_observations");

            migrationBuilder.DropTable(
                name: "wb_recommendation_runs");

            migrationBuilder.DropColumn(
                name: "nomenclature_id",
                table: "wb_norm_revisions");

            migrationBuilder.DropColumn(
                name: "recommendation_fields_verified",
                table: "campaign_nomenclature_statistics");
        }
    }
}

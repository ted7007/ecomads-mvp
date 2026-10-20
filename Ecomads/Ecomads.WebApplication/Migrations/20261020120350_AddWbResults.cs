using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wb_result_measurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetectedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Before_Start = table.Column<DateOnly>(type: "date", nullable: false),
                    Before_End = table.Column<DateOnly>(type: "date", nullable: false),
                    After_Start = table.Column<DateOnly>(type: "date", nullable: false),
                    After_End = table.Column<DateOnly>(type: "date", nullable: false),
                    MembersJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Savings = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InterruptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ActionPreserved = table.Column<bool>(type: "boolean", nullable: false),
                    ContinuationCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContinuationReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_result_measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_result_measurements_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_result_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_result_observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_result_observations_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_result_recommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKey = table.Column<string>(type: "text", nullable: false),
                    FirstShownAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastShownAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CampaignName = table.Column<string>(type: "text", nullable: false),
                    MeasurementId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaselineJson = table.Column<string>(type: "jsonb", nullable: true),
                    Legacy = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_result_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_result_recommendations_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_result_recommendations_wb_result_measurements_Measuremen~",
                        column: x => x.MeasurementId,
                        principalTable: "wb_result_measurements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wb_result_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeasurementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_result_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_result_revisions_wb_result_measurements_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "wb_result_measurements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_result_exposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "text", nullable: false),
                    ShownAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CardJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_result_exposures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_result_exposures_wb_recommendation_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "wb_recommendation_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wb_result_exposures_wb_result_recommendations_Recommendatio~",
                        column: x => x.RecommendationId,
                        principalTable: "wb_result_recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_result_exposures_RecommendationId_RunId_EvaluationId_Rul~",
                table: "wb_result_exposures",
                columns: new[] { "RecommendationId", "RunId", "EvaluationId", "RuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wb_result_exposures_RunId",
                table: "wb_result_exposures",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_wb_result_measurements_StoreId_DetectedDate",
                table: "wb_result_measurements",
                columns: new[] { "StoreId", "DetectedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_result_observations_CampaignId_AttemptedAtUtc",
                table: "wb_result_observations",
                columns: new[] { "CampaignId", "AttemptedAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wb_result_recommendations_MeasurementId",
                table: "wb_result_recommendations",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_wb_result_recommendations_StoreId_SubjectKey_FirstShownAtUtc",
                table: "wb_result_recommendations",
                columns: new[] { "StoreId", "SubjectKey", "FirstShownAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_result_revisions_MeasurementId_Number",
                table: "wb_result_revisions",
                columns: new[] { "MeasurementId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_result_exposures");

            migrationBuilder.DropTable(
                name: "wb_result_observations");

            migrationBuilder.DropTable(
                name: "wb_result_revisions");

            migrationBuilder.DropTable(
                name: "wb_result_recommendations");

            migrationBuilder.DropTable(
                name: "wb_result_measurements");
        }
    }
}

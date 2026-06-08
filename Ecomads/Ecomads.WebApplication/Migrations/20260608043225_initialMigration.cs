using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class initialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sellers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sellers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    marketplace = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Wildberries"),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    api_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_sync_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.id);
                    table.ForeignKey(
                        name: "FK_stores_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "compaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    budget = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compaigns", x => x.id);
                    table.ForeignKey(
                        name: "FK_compaigns_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "compaign_statistics",
                columns: table => new
                {
                    compaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    revenue = table.Column<float>(type: "real", nullable: false),
                    spend = table.Column<float>(type: "real", nullable: false),
                    clicks = table.Column<float>(type: "real", nullable: false),
                    ctr = table.Column<float>(type: "real", nullable: false),
                    drr = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compaign_statistics", x => new { x.compaign_id, x.start_date, x.end_date, x.type });
                    table.ForeignKey(
                        name: "FK_compaign_statistics_compaigns_compaign_id",
                        column: x => x.compaign_id,
                        principalTable: "compaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "keyword_statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    compaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    phrase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: true),
                    cpm = table.Column<decimal>(type: "numeric", nullable: true),
                    avg_position = table.Column<double>(type: "double precision", nullable: true),
                    impressions = table.Column<int>(type: "integer", nullable: true),
                    clicks = table.Column<int>(type: "integer", nullable: true),
                    ctr = table.Column<double>(type: "double precision", nullable: true),
                    spend = table.Column<decimal>(type: "numeric", nullable: true),
                    orders = table.Column<int>(type: "integer", nullable: true),
                    revenue = table.Column<decimal>(type: "numeric", nullable: true),
                    drr = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_keyword_statistics", x => x.id);
                    table.ForeignKey(
                        name: "FK_keyword_statistics_compaigns_compaign_id",
                        column: x => x.compaign_id,
                        principalTable: "compaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    goal = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    full_response = table.Column<string>(type: "text", nullable: false),
                    problem = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    recommendation_text = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    expected_effect = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    request_metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "новая"),
                    status_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_comment = table.Column<string>(type: "text", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendations_compaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "compaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_insights",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    recommendation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    insight_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    priority_score = table.Column<double>(type: "double precision", nullable: false),
                    priority_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    confidence_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recommended_action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    decision_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_comment = table.Column<string>(type: "text", nullable: true),
                    expected_effect_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    expected_effect_money = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    expected_effect_text = table.Column<string>(type: "text", nullable: false),
                    actual_effect_money = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    actual_effect_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    metrics = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    reason_codes = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    allowed_actions = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    forbidden_actions = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_insights", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendation_insights_compaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "compaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recommendation_insights_recommendations_recommendation_run_id",
                        column: x => x.recommendation_run_id,
                        principalTable: "recommendations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_compaigns_store_id",
                table: "compaigns",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_keyword_statistics_compaign_id",
                table: "keyword_statistics",
                column: "compaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_insights_campaign_id_entity_type_entity_id",
                table: "recommendation_insights",
                columns: new[] { "campaign_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_insights_decision_status",
                table: "recommendation_insights",
                column: "decision_status");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_insights_recommendation_run_id",
                table: "recommendation_insights",
                column: "recommendation_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_campaign_id",
                table: "recommendations",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_sellers_email",
                table: "sellers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stores_seller_id",
                table: "stores",
                column: "seller_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compaign_statistics");

            migrationBuilder.DropTable(
                name: "keyword_statistics");

            migrationBuilder.DropTable(
                name: "recommendation_insights");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "compaigns");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropTable(
                name: "sellers");
        }
    }
}

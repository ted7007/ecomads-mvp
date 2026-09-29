using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
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
                    bothub_caps = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    estimated_cost_rub = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
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
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_demo_user = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    access_type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    demo_status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    demo_started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    demo_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    demo_feedback_submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    mvp_access_granted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sellers", x => x.id);
                });

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
                    table.ForeignKey(
                        name: "FK_product_usage_events_llm_usage_events_llm_usage_id",
                        column: x => x.llm_usage_id,
                        principalTable: "llm_usage_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "demo_feedbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    general_comment = table.Column<string>(type: "text", nullable: false),
                    dashboard_clarity_score = table.Column<int>(type: "integer", nullable: false),
                    recommendations_usefulness_score = table.Column<int>(type: "integer", nullable: false),
                    wrong_or_questionable_recommendations = table.Column<string>(type: "text", nullable: true),
                    most_useful_feature = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    missing_for_regular_usage = table.Column<string>(type: "text", nullable: true),
                    continue_testing_answer = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    willing_to_pay_answer = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_feedbacks", x => x.id);
                    table.ForeignKey(
                        name: "FK_demo_feedbacks_sellers_user_id",
                        column: x => x.user_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    wb_campaign_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    budget = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.id);
                    table.ForeignKey(
                        name: "FK_campaigns_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "nomenclatures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wb_nomenclature_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nomenclatures", x => x.id);
                    table.ForeignKey(
                        name: "FK_nomenclatures_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign_statistics",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    revenue = table.Column<float>(type: "real", nullable: false),
                    spend = table.Column<float>(type: "real", nullable: false),
                    clicks = table.Column<float>(type: "real", nullable: false),
                    ctr = table.Column<float>(type: "real", nullable: false),
                    drr = table.Column<float>(type: "real", nullable: false),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    carts = table.Column<int>(type: "integer", nullable: false),
                    orders = table.Column<int>(type: "integer", nullable: false),
                    cancellations = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_statistics", x => new { x.campaign_id, x.start_date, x.end_date, x.type });
                    table.ForeignKey(
                        name: "FK_campaign_statistics_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "keyword_statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    phrase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_phrase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    bid_cpm = table.Column<decimal>(type: "numeric", nullable: true),
                    frequency = table.Column<int>(type: "integer", nullable: true),
                    cpm = table.Column<decimal>(type: "numeric", nullable: true),
                    avg_position = table.Column<double>(type: "double precision", nullable: true),
                    impressions = table.Column<int>(type: "integer", nullable: true),
                    clicks = table.Column<int>(type: "integer", nullable: true),
                    ctr = table.Column<double>(type: "double precision", nullable: true),
                    spend = table.Column<decimal>(type: "numeric", nullable: true),
                    baskets = table.Column<int>(type: "integer", nullable: true),
                    orders = table.Column<int>(type: "integer", nullable: true),
                    cpc = table.Column<decimal>(type: "numeric", nullable: true),
                    cpo = table.Column<decimal>(type: "numeric", nullable: true),
                    revenue = table.Column<decimal>(type: "numeric", nullable: true),
                    drr = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_keyword_statistics", x => x.id);
                    table.ForeignKey(
                        name: "FK_keyword_statistics_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
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
                        name: "FK_recommendations_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign_nomenclature_statistics",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nomenclature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    spend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    revenue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    clicks = table.Column<int>(type: "integer", nullable: false),
                    carts = table.Column<int>(type: "integer", nullable: false),
                    orders = table.Column<int>(type: "integer", nullable: false),
                    cancellations = table.Column<int>(type: "integer", nullable: false),
                    ctr = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cr = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cpm = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cpc = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cpo = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    average_position = table.Column<decimal>(type: "numeric(18,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_nomenclature_statistics", x => new { x.campaign_id, x.nomenclature_id, x.start_date, x.end_date });
                    table.ForeignKey(
                        name: "FK_campaign_nomenclature_statistics_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_campaign_nomenclature_statistics_nomenclatures_nomenclature~",
                        column: x => x.nomenclature_id,
                        principalTable: "nomenclatures",
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
                        name: "FK_recommendation_insights_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
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
                name: "IX_campaign_nomenclature_statistics_nomenclature_id_start_date~",
                table: "campaign_nomenclature_statistics",
                columns: new[] { "nomenclature_id", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_store_id_wb_campaign_id",
                table: "campaigns",
                columns: new[] { "store_id", "wb_campaign_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_feedbacks_user_id",
                table: "demo_feedbacks",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_keyword_statistics_campaign_id_start_date_end_date_normaliz~",
                table: "keyword_statistics",
                columns: new[] { "campaign_id", "start_date", "end_date", "normalized_phrase" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_nomenclatures_store_id_wb_nomenclature_id",
                table: "nomenclatures",
                columns: new[] { "store_id", "wb_nomenclature_id" },
                unique: true);

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
                name: "campaign_nomenclature_statistics");

            migrationBuilder.DropTable(
                name: "campaign_statistics");

            migrationBuilder.DropTable(
                name: "demo_feedbacks");

            migrationBuilder.DropTable(
                name: "keyword_statistics");

            migrationBuilder.DropTable(
                name: "product_usage_events");

            migrationBuilder.DropTable(
                name: "recommendation_insights");

            migrationBuilder.DropTable(
                name: "nomenclatures");

            migrationBuilder.DropTable(
                name: "llm_usage_events");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropTable(
                name: "sellers");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class InitialWbSchema : Migration
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
                name: "telegram_bot_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    next_update_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_bot_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "demo_feedbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    general_comment = table.Column<string>(type: "text", nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
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
                    api_key = table.Column<string>(type: "text", nullable: true),
                    token_last_four = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    token_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                name: "telegram_chats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_chats", x => x.id);
                    table.ForeignKey(
                        name: "FK_telegram_chats_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_link_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_link_codes", x => x.id);
                    table.ForeignKey(
                        name: "FK_telegram_link_codes_sellers_seller_id",
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
                    wb_status = table.Column<int>(type: "integer", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "wb_store_norms",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_drr = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    min_clicks = table.Column<int>(type: "integer", nullable: false),
                    min_spend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    min_orders = table.Column<int>(type: "integer", nullable: false),
                    deviation_percent = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_store_norms", x => x.store_id);
                    table.ForeignKey(
                        name: "FK_wb_store_norms_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_sync_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    campaign_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    pair_ids_json = table.Column<string>(type: "jsonb", nullable: true),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "fullstats"),
                    next_campaign_offset = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_request_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_sync_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_wb_sync_jobs_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "FK_telegram_deliveries_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_telegram_deliveries_telegram_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "telegram_chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign_statistics",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revenue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    spend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    clicks = table.Column<int>(type: "integer", nullable: false),
                    ctr = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    drr = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    carts = table.Column<int>(type: "integer", nullable: false),
                    orders = table.Column<int>(type: "integer", nullable: false),
                    cancellations = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_statistics", x => new { x.campaign_id, x.date });
                    table.ForeignKey(
                        name: "FK_campaign_statistics_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_campaign_norms",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    goal = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    target_drr = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    min_clicks = table.Column<int>(type: "integer", nullable: true),
                    min_spend = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    min_orders = table.Column<int>(type: "integer", nullable: true),
                    deviation_percent = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_campaign_norms", x => x.campaign_id);
                    table.ForeignKey(
                        name: "FK_wb_campaign_norms_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_norm_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_norm_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_wb_norm_revisions_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_norm_revisions_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign_nomenclature_statistics",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nomenclature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_campaign_nomenclature_statistics", x => new { x.campaign_id, x.nomenclature_id, x.date });
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
                name: "wb_cluster_statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nomenclature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    cluster_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    spend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    views = table.Column<int>(type: "integer", nullable: true),
                    clicks = table.Column<int>(type: "integer", nullable: true),
                    carts = table.Column<int>(type: "integer", nullable: true),
                    orders = table.Column<int>(type: "integer", nullable: true),
                    ordered_products = table.Column<int>(type: "integer", nullable: true),
                    average_position = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cpc = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cpm = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ctr = table.Column<decimal>(type: "numeric(18,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_cluster_statistics", x => x.id);
                    table.ForeignKey(
                        name: "FK_wb_cluster_statistics_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_cluster_statistics_nomenclatures_nomenclature_id",
                        column: x => x.nomenclature_id,
                        principalTable: "nomenclatures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_campaign_nomenclature_statistics_nomenclature_id_date",
                table: "campaign_nomenclature_statistics",
                columns: new[] { "nomenclature_id", "date" });

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
                name: "IX_nomenclatures_store_id_wb_nomenclature_id",
                table: "nomenclatures",
                columns: new[] { "store_id", "wb_nomenclature_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sellers_email",
                table: "sellers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stores_marketplace_external_id",
                table: "stores",
                columns: new[] { "marketplace", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stores_seller_id",
                table: "stores",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_chats_chat_id",
                table: "telegram_chats",
                column: "chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_chats_seller_id",
                table: "telegram_chats",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_deliveries_chat_id",
                table: "telegram_deliveries",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_deliveries_store_id_chat_id_report_date",
                table: "telegram_deliveries",
                columns: new[] { "store_id", "chat_id", "report_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_code_hash",
                table: "telegram_link_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_seller_id",
                table: "telegram_link_codes",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "IX_wb_cluster_statistics_campaign_id_nomenclature_id_date_clus~",
                table: "wb_cluster_statistics",
                columns: new[] { "campaign_id", "nomenclature_id", "date", "cluster_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wb_cluster_statistics_nomenclature_id",
                table: "wb_cluster_statistics",
                column: "nomenclature_id");

            migrationBuilder.CreateIndex(
                name: "IX_wb_norm_revisions_campaign_id",
                table: "wb_norm_revisions",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_wb_norm_revisions_store_id_campaign_id_version",
                table: "wb_norm_revisions",
                columns: new[] { "store_id", "campaign_id", "version" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_sync_jobs_store_id_status_next_attempt_at_utc",
                table: "wb_sync_jobs",
                columns: new[] { "store_id", "status", "next_attempt_at_utc" });
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
                name: "telegram_bot_state");

            migrationBuilder.DropTable(
                name: "telegram_deliveries");

            migrationBuilder.DropTable(
                name: "telegram_link_codes");

            migrationBuilder.DropTable(
                name: "wb_campaign_norms");

            migrationBuilder.DropTable(
                name: "wb_cluster_statistics");

            migrationBuilder.DropTable(
                name: "wb_norm_revisions");

            migrationBuilder.DropTable(
                name: "wb_store_norms");

            migrationBuilder.DropTable(
                name: "wb_sync_jobs");

            migrationBuilder.DropTable(
                name: "telegram_chats");

            migrationBuilder.DropTable(
                name: "nomenclatures");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropTable(
                name: "sellers");
        }
    }
}

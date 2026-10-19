using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbResponseJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wb_ingestion_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AcceptedValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_ingestion_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wb_response_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    RequestUri = table.Column<string>(type: "text", nullable: false),
                    RequestBody = table.Column<string>(type: "text", nullable: true),
                    JobStateJson = table.Column<string>(type: "text", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    RawBodyBase64 = table.Column<string>(type: "text", nullable: true),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_response_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wb_response_snapshots_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_source_quality",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Entity = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ProposedValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_source_quality", x => new { x.StoreId, x.Source, x.Entity, x.StartDate, x.EndDate });
                    table.ForeignKey(
                        name: "FK_wb_source_quality_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wb_cost_observations",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    RawTime = table.Column<string>(type: "text", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MoscowDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    State = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_cost_observations", x => new { x.SnapshotId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_wb_cost_observations_wb_response_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "wb_response_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_cost_observations_StoreId_MoscowDate",
                table: "wb_cost_observations",
                columns: new[] { "StoreId", "MoscowDate" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_ingestion_decisions_StoreId_Date",
                table: "wb_ingestion_decisions",
                columns: new[] { "StoreId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_response_snapshots_JobId_Page",
                table: "wb_response_snapshots",
                columns: new[] { "JobId", "Page" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_response_snapshots_StoreId_RequestedAtUtc",
                table: "wb_response_snapshots",
                columns: new[] { "StoreId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_source_quality_AcceptedSnapshotId",
                table: "wb_source_quality",
                column: "AcceptedSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_wb_source_quality_SnapshotId",
                table: "wb_source_quality",
                column: "SnapshotId");
            migrationBuilder.Sql("""
                INSERT INTO wb_source_quality
                    ("StoreId", "Source", "Entity", "StartDate", "EndDate", "State", "Reason", "LastAttemptAtUtc", "AcceptedAtUtc", "PreviousValue")
                SELECT store_id, 'expenses', '', date, date, 'legacy',
                    'Значение получено прежним обработчиком; требуется перепроверка.', loaded_at_utc, loaded_at_utc, spend
                FROM wb_store_daily_spend;
                INSERT INTO wb_source_quality
                    ("StoreId", "Source", "Entity", "StartDate", "EndDate", "State", "Reason", "LastAttemptAtUtc", "AcceptedAtUtc", "PreviousValue")
                SELECT store_id, 'orders', '', date, date, 'legacy',
                    'Значение получено прежним обработчиком; требуется перепроверка.', loaded_at_utc, loaded_at_utc, order_sum
                FROM wb_store_daily_orders;
                INSERT INTO wb_source_quality
                    ("StoreId", "Source", "Entity", "StartDate", "EndDate", "State", "Reason", "LastAttemptAtUtc", "AcceptedAtUtc", "PreviousValue")
                SELECT c.store_id, 'fullstats', d.campaign_id::text, d.date, d.date, 'legacy',
                    'Статистика получена прежним обработчиком; требуется перепроверка.',
                    COALESCE(d.last_attempt_at_utc, d.checked_at_utc), d.checked_at_utc, d.spend
                FROM wb_campaign_daily_checks d JOIN campaigns c ON c.id = d.campaign_id;
                INSERT INTO wb_source_quality
                    ("StoreId", "Source", "Entity", "StartDate", "EndDate", "State", "Reason", "LastAttemptAtUtc", "AcceptedAtUtc")
                SELECT store_id, 'jam', nomenclature_id::text, start_date, end_date, 'legacy',
                    'Ограниченный список запросов получен прежним обработчиком.', MAX(loaded_at_utc), MAX(loaded_at_utc)
                FROM wb_jam_search_queries GROUP BY store_id, nomenclature_id, start_date, end_date;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Roll back the application image, not the audit schema. Reapplying an undone
            // migration would otherwise destroy or conflict with the retained evidence.
            throw new NotSupportedException("Журнал WB сохраняется при откате. Откатывайте образ приложения без отката этой миграции.");
        }
    }
}

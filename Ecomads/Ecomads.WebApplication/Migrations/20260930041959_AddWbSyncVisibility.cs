using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbSyncVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at_utc",
                table: "wb_sync_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "retried_from_job_id",
                table: "wb_sync_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stage",
                table: "wb_sync_jobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "queued");

            migrationBuilder.AddColumn<DateTime>(
                name: "started_at_utc",
                table: "wb_sync_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wait_reason",
                table: "wb_sync_jobs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wb_sync_job_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    processed_count = table.Column<int>(type: "integer", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_sync_job_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_wb_sync_job_events_wb_sync_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "wb_sync_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_sync_jobs_store_id",
                table: "wb_sync_jobs",
                column: "store_id",
                unique: true,
                filter: "status IN ('pending', 'running')");

            migrationBuilder.CreateIndex(
                name: "IX_wb_sync_job_events_job_id_occurred_at_utc",
                table: "wb_sync_job_events",
                columns: new[] { "job_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_sync_job_events");

            migrationBuilder.DropIndex(
                name: "IX_wb_sync_jobs_store_id",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "completed_at_utc",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "retried_from_job_id",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "stage",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "started_at_utc",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "wait_reason",
                table: "wb_sync_jobs");
        }
    }
}

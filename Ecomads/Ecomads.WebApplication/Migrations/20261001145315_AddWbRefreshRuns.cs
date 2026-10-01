using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbRefreshRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                table: "wb_sync_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "campaigns_refreshed_at_utc",
                table: "stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wb_sync_jobs_store_id_run_id",
                table: "wb_sync_jobs",
                columns: new[] { "store_id", "run_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wb_sync_jobs_store_id_run_id",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "run_id",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "campaigns_refreshed_at_utc",
                table: "stores");
        }
    }
}

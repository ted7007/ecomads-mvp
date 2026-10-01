using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class PerKindWbSyncSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wb_sync_jobs_store_id",
                table: "wb_sync_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_wb_sync_jobs_store_id_kind",
                table: "wb_sync_jobs",
                columns: new[] { "store_id", "kind" },
                unique: true,
                filter: "status IN ('pending', 'running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wb_sync_jobs_store_id_kind",
                table: "wb_sync_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_wb_sync_jobs_store_id",
                table: "wb_sync_jobs",
                column: "store_id",
                unique: true,
                filter: "status IN ('pending', 'running')");
        }
    }
}

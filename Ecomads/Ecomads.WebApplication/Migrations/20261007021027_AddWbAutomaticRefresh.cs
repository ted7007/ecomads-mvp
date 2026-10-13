using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbAutomaticRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "auto_history_started_at_utc",
                table: "stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "auto_refresh_blocked",
                table: "stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "auto_refresh_enabled",
                table: "stores",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "wb_refresh_schedules",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_slot_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    planned_slot_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_refresh_schedules", x => new { x.store_id, x.task });
                    table.ForeignKey(
                        name: "FK_wb_refresh_schedules_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_refresh_schedules");

            migrationBuilder.DropColumn(
                name: "auto_history_started_at_utc",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "auto_refresh_blocked",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "auto_refresh_enabled",
                table: "stores");
        }
    }
}

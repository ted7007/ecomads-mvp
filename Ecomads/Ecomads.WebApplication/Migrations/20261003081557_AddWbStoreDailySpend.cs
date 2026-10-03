using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbStoreDailySpend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wb_store_daily_spend",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    spend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    loaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_store_daily_spend", x => new { x.store_id, x.date });
                    table.ForeignKey(
                        name: "FK_wb_store_daily_spend_stores_store_id",
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
                name: "wb_store_daily_spend");
        }
    }
}

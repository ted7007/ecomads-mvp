using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbStoreDailyOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wb_store_daily_orders",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    order_count = table.Column<int>(type: "integer", nullable: false),
                    order_sum = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    open_count = table.Column<int>(type: "integer", nullable: false),
                    cart_count = table.Column<int>(type: "integer", nullable: false),
                    buyout_count = table.Column<int>(type: "integer", nullable: false),
                    buyout_sum = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    loaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_store_daily_orders", x => new { x.store_id, x.date });
                    table.ForeignKey(
                        name: "FK_wb_store_daily_orders_stores_store_id",
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
                name: "wb_store_daily_orders");
        }
    }
}

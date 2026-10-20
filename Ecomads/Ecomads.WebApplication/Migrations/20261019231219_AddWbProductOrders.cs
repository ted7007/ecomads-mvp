using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbProductOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastProgressAtUtc",
                table: "wb_sync_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductOrdersError",
                table: "wb_response_snapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductOrdersVersion",
                table: "wb_response_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "wb_product_order_days",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProductCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_product_order_days", x => new { x.StoreId, x.Date });
                    table.ForeignKey(
                        name: "FK_wb_product_order_days_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_product_order_days_wb_response_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "wb_response_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wb_product_daily_orders",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    NmId = table.Column<long>(type: "bigint", nullable: false),
                    OrderSum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_product_daily_orders", x => new { x.StoreId, x.Date, x.NmId });
                    table.ForeignKey(
                        name: "FK_wb_product_daily_orders_wb_product_order_days_StoreId_Date",
                        columns: x => new { x.StoreId, x.Date },
                        principalTable: "wb_product_order_days",
                        principalColumns: new[] { "StoreId", "Date" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_product_daily_orders_StoreId_NmId_Date",
                table: "wb_product_daily_orders",
                columns: new[] { "StoreId", "NmId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_wb_product_order_days_SnapshotId",
                table: "wb_product_order_days",
                column: "SnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_product_daily_orders");

            migrationBuilder.DropTable(
                name: "wb_product_order_days");

            migrationBuilder.DropColumn(
                name: "LastProgressAtUtc",
                table: "wb_sync_jobs");

            migrationBuilder.DropColumn(
                name: "ProductOrdersError",
                table: "wb_response_snapshots");

            migrationBuilder.DropColumn(
                name: "ProductOrdersVersion",
                table: "wb_response_snapshots");
        }
    }
}

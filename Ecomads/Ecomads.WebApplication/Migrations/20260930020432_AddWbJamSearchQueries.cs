using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbJamSearchQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "jam_checked_at_utc",
                table: "stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "jam_status",
                table: "stores",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.CreateTable(
                name: "wb_jam_search_queries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nomenclature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    search_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    frequency = table.Column<long>(type: "bigint", nullable: true),
                    week_frequency = table.Column<long>(type: "bigint", nullable: true),
                    average_position = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    median_position = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    open_card = table.Column<long>(type: "bigint", nullable: true),
                    add_to_cart = table.Column<long>(type: "bigint", nullable: true),
                    orders = table.Column<long>(type: "bigint", nullable: true),
                    loaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_jam_search_queries", x => x.id);
                    table.ForeignKey(
                        name: "FK_wb_jam_search_queries_nomenclatures_nomenclature_id",
                        column: x => x.nomenclature_id,
                        principalTable: "nomenclatures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_jam_search_queries_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_jam_search_queries_nomenclature_id",
                table: "wb_jam_search_queries",
                column: "nomenclature_id");

            migrationBuilder.CreateIndex(
                name: "IX_wb_jam_search_queries_store_id_nomenclature_id_start_date_e~",
                table: "wb_jam_search_queries",
                columns: new[] { "store_id", "nomenclature_id", "start_date", "end_date", "search_text" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_jam_search_queries");

            migrationBuilder.DropColumn(
                name: "jam_checked_at_utc",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "jam_status",
                table: "stores");
        }
    }
}

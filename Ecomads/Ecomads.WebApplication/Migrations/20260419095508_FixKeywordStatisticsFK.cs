using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class FixKeywordStatisticsFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keyword_statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    compaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    phrase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: true),
                    cpm = table.Column<decimal>(type: "numeric", nullable: true),
                    avg_position = table.Column<double>(type: "double precision", nullable: true),
                    impressions = table.Column<int>(type: "integer", nullable: true),
                    clicks = table.Column<int>(type: "integer", nullable: true),
                    ctr = table.Column<double>(type: "double precision", nullable: true),
                    spend = table.Column<decimal>(type: "numeric", nullable: true),
                    orders = table.Column<int>(type: "integer", nullable: true),
                    revenue = table.Column<decimal>(type: "numeric", nullable: true),
                    drr = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_keyword_statistics", x => x.id);
                    table.ForeignKey(
                        name: "FK_keyword_statistics_compaigns_compaign_id",
                        column: x => x.compaign_id,
                        principalTable: "compaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_keyword_statistics_compaign_id",
                table: "keyword_statistics",
                column: "compaign_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keyword_statistics");
        }
    }
}

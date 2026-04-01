using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sellers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sellers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "compaign_statistics",
                columns: table => new
                {
                    compaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    revenue = table.Column<float>(type: "real", nullable: false),
                    spend = table.Column<float>(type: "real", nullable: false),
                    clicks = table.Column<float>(type: "real", nullable: false),
                    ctr = table.Column<float>(type: "real", nullable: false),
                    drr = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compaign_statistics", x => new { x.compaign_id, x.start_date, x.end_date, x.type });
                    table.ForeignKey(
                        name: "FK_compaign_statistics_compaigns_compaign_id",
                        column: x => x.compaign_id,
                        principalTable: "compaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compaign_statistics");

            migrationBuilder.DropTable(
                name: "sellers");

            migrationBuilder.DropTable(
                name: "compaigns");
        }
    }
}

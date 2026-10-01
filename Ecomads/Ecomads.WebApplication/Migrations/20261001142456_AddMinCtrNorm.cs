using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddMinCtrNorm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "min_ctr",
                table: "wb_store_norms",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.AddColumn<decimal>(
                name: "min_ctr",
                table: "wb_campaign_norms",
                type: "numeric(8,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "min_ctr",
                table: "wb_store_norms");

            migrationBuilder.DropColumn(
                name: "min_ctr",
                table: "wb_campaign_norms");
        }
    }
}

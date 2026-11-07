using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoRegistrationSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DemoBudgetSegment",
                table: "sellers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DemoJamSegment",
                table: "sellers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DemoStoresSegment",
                table: "sellers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DemoBudgetSegment",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "DemoJamSegment",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "DemoStoresSegment",
                table: "sellers");
        }
    }
}

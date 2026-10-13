using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddWbSparseObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_attempt_at_utc",
                table: "wb_campaign_daily_checks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_attempt_result",
                table: "wb_campaign_daily_checks",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_attempt_at_utc",
                table: "wb_campaign_daily_checks");

            migrationBuilder.DropColumn(
                name: "last_attempt_result",
                table: "wb_campaign_daily_checks");
        }
    }
}

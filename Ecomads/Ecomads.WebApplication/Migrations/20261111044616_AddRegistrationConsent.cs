using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "registration_consent_at_utc",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_consent_method",
                table: "sellers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_consent_text",
                table: "sellers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_consent_version",
                table: "sellers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "registration_consent_at_utc",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "registration_consent_method",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "registration_consent_text",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "registration_consent_version",
                table: "sellers");
        }
    }
}

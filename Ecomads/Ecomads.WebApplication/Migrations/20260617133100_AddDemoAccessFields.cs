using System;
using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    [DbContext(typeof(EcomadsDbContext))]
    [Migration("20260617133100_AddDemoAccessFields")]
    public partial class AddDemoAccessFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "access_type",
                table: "sellers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "demo_expires_at_utc",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "demo_feedback_submitted_at_utc",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "demo_started_at_utc",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "demo_status",
                table: "sellers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_demo_user",
                table: "sellers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "mvp_access_granted_at_utc",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "access_type",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "demo_expires_at_utc",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "demo_feedback_submitted_at_utc",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "demo_started_at_utc",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "demo_status",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "is_demo_user",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "mvp_access_granted_at_utc",
                table: "sellers");
        }
    }
}

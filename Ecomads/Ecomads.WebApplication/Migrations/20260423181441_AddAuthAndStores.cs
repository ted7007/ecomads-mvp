using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthAndStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "last_login_at",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "sellers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "sellers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "budget",
                table: "compaigns",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "compaigns",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "compaigns",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "end_date",
                table: "compaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "start_date",
                table: "compaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                table: "compaigns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    marketplace = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Wildberries"),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    api_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_sync_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.id);
                    table.ForeignKey(
                        name: "FK_stores_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sellers_email",
                table: "sellers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_compaigns_store_id",
                table: "compaigns",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "IX_stores_seller_id",
                table: "stores",
                column: "seller_id");

            migrationBuilder.AddForeignKey(
                name: "FK_compaigns_stores_store_id",
                table: "compaigns",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_compaigns_stores_store_id",
                table: "compaigns");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropIndex(
                name: "IX_sellers_email",
                table: "sellers");

            migrationBuilder.DropIndex(
                name: "IX_compaigns_store_id",
                table: "compaigns");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "last_login_at",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "budget",
                table: "compaigns");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "compaigns");

            migrationBuilder.DropColumn(
                name: "description",
                table: "compaigns");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "compaigns");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "compaigns");

            migrationBuilder.DropColumn(
                name: "store_id",
                table: "compaigns");
        }
    }
}

using System;
using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    [DbContext(typeof(EcomadsDbContext))]
    [Migration("20260617140500_AddDemoFeedbacks")]
    public partial class AddDemoFeedbacks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demo_feedbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    general_comment = table.Column<string>(type: "text", nullable: false),
                    dashboard_clarity_score = table.Column<int>(type: "integer", nullable: false),
                    recommendations_usefulness_score = table.Column<int>(type: "integer", nullable: false),
                    wrong_or_questionable_recommendations = table.Column<string>(type: "text", nullable: true),
                    most_useful_feature = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    missing_for_regular_usage = table.Column<string>(type: "text", nullable: true),
                    continue_testing_answer = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    willing_to_pay_answer = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_feedbacks", x => x.id);
                    table.ForeignKey(
                        name: "FK_demo_feedbacks_sellers_user_id",
                        column: x => x.user_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demo_feedbacks_user_id",
                table: "demo_feedbacks",
                column: "user_id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demo_feedbacks");
        }
    }
}

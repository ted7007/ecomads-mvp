using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    goal = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    full_response = table.Column<string>(type: "text", nullable: false),
                    problem = table.Column<string>(type: "text", nullable: false),
                    recommendation_text = table.Column<string>(type: "text", nullable: false),
                    expected_effect = table.Column<string>(type: "text", nullable: false),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false),
                    request_metadata = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_comment = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendations_compaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "compaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_campaign_id",
                table: "recommendations",
                column: "campaign_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendations");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWbArticleNorms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wb_article_norms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wb_article_norms",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomenclatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    LaunchDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetDrr = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wb_article_norms", x => new { x.CampaignId, x.NomenclatureId });
                    table.ForeignKey(
                        name: "FK_wb_article_norms_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wb_article_norms_nomenclatures_NomenclatureId",
                        column: x => x.NomenclatureId,
                        principalTable: "nomenclatures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wb_article_norms_NomenclatureId",
                table: "wb_article_norms",
                column: "NomenclatureId");
        }
    }
}

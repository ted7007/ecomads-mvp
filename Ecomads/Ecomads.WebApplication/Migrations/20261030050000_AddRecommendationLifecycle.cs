using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations;

public partial class AddRecommendationLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "LastConditionDate", table: "wb_result_recommendations", type: "date", nullable: true);
        migrationBuilder.AddColumn<int>(name: "ClearConditionNights", table: "wb_result_recommendations", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>(name: "ArchivedAtUtc", table: "wb_result_recommendations", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "OtherChangeAtUtc", table: "wb_result_recommendations", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LastConditionDate", table: "wb_result_recommendations");
        migrationBuilder.DropColumn(name: "ClearConditionNights", table: "wb_result_recommendations");
        migrationBuilder.DropColumn(name: "ArchivedAtUtc", table: "wb_result_recommendations");
        migrationBuilder.DropColumn(name: "OtherChangeAtUtc", table: "wb_result_recommendations");
    }
}

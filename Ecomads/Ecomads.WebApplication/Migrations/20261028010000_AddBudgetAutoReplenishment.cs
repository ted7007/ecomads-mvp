using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecomads.WebApplication.Migrations;

public partial class AddBudgetAutoReplenishment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "budget_auto_replenishment_enabled", table: "wb_campaign_norms",
            type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "ReasonCode", table: "wb_snapshot_decisions", type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReasonCode", table: "wb_snapshot_decisions");
        migrationBuilder.DropColumn(name: "budget_auto_replenishment_enabled", table: "wb_campaign_norms");
    }
}

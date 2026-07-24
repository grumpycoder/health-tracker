using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class GoalsWaterAddedSugar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AddedSugarG",
                table: "MealEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AddedSugarG",
                table: "DrinkEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaterOz",
                table: "DailyLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedSugarG",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "AddedSugarG",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "WaterOz",
                table: "DailyLogs");
        }
    }
}

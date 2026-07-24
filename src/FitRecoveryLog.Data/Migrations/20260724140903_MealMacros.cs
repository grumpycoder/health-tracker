using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class MealMacros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Calories",
                table: "MealEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CarbsG",
                table: "MealEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FatG",
                table: "MealEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FiberG",
                table: "MealEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProteinG",
                table: "MealEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SodiumMg",
                table: "MealEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SugarG",
                table: "MealEntries",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Calories",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "CarbsG",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "FatG",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "FiberG",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "ProteinG",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "SodiumMg",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "SugarG",
                table: "MealEntries");
        }
    }
}

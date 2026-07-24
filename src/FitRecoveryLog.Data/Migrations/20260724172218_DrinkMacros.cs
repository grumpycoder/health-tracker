using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class DrinkMacros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Calories",
                table: "DrinkEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CarbsG",
                table: "DrinkEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FatG",
                table: "DrinkEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FiberG",
                table: "DrinkEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProteinG",
                table: "DrinkEntries",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SodiumMg",
                table: "DrinkEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SugarG",
                table: "DrinkEntries",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Calories",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "CarbsG",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "FatG",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "FiberG",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "ProteinG",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "SodiumMg",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "SugarG",
                table: "DrinkEntries");
        }
    }
}

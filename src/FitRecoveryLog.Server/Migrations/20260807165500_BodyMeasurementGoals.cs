using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Server.Migrations
{
    /// <inheritdoc />
    public partial class BodyMeasurementGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "GoalArmsInches",
                table: "GoalSettings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GoalBodyFatPercent",
                table: "GoalSettings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GoalChestInches",
                table: "GoalSettings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GoalThighsInches",
                table: "GoalSettings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GoalWaistInches",
                table: "GoalSettings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GoalWeightLbs",
                table: "GoalSettings",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoalArmsInches",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "GoalBodyFatPercent",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "GoalChestInches",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "GoalThighsInches",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "GoalWaistInches",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "GoalWeightLbs",
                table: "GoalSettings");
        }
    }
}

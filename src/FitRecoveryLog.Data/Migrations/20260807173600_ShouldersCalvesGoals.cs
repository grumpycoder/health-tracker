using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShouldersCalvesGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "GoalCalvesInches",
                table: "GoalSettings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GoalShouldersInches",
                table: "GoalSettings",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoalCalvesInches",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "GoalShouldersInches",
                table: "GoalSettings");
        }
    }
}

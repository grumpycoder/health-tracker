using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Server.Migrations
{
    /// <inheritdoc />
    public partial class HealthEnergy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveEnergyKcal",
                table: "DailyLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestingEnergyKcal",
                table: "DailyLogs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveEnergyKcal",
                table: "DailyLogs");

            migrationBuilder.DropColumn(
                name: "RestingEnergyKcal",
                table: "DailyLogs");
        }
    }
}

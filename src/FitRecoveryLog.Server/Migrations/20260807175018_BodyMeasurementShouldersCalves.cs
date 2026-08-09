using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Server.Migrations
{
    /// <inheritdoc />
    public partial class BodyMeasurementShouldersCalves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CalvesInches",
                table: "BodyMeasurements",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShouldersInches",
                table: "BodyMeasurements",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalvesInches",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "ShouldersInches",
                table: "BodyMeasurements");
        }
    }
}

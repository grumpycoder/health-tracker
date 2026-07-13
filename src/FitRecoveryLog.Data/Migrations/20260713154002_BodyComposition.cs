using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class BodyComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BasalMetabolicRate",
                table: "BodyMeasurements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BodyFatPercent",
                table: "BodyMeasurements",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BodyWaterPercent",
                table: "BodyMeasurements",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetabolicAge",
                table: "BodyMeasurements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MuscleMassLbs",
                table: "BodyMeasurements",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VisceralFat",
                table: "BodyMeasurements",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BasalMetabolicRate",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "BodyFatPercent",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "BodyWaterPercent",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "MetabolicAge",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "MuscleMassLbs",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "VisceralFat",
                table: "BodyMeasurements");
        }
    }
}

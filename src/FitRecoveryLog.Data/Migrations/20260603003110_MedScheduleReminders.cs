using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class MedScheduleReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Frequency",
                table: "MedicationSchedules",
                newName: "EndDate");

            migrationBuilder.AddColumn<int>(
                name: "NotificationId",
                table: "MedicationSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ReminderTime",
                table: "MedicationSchedules",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "Repeat",
                table: "MedicationSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "MedicationSchedules",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationId",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "ReminderTime",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "Repeat",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "MedicationSchedules");

            migrationBuilder.RenameColumn(
                name: "EndDate",
                table: "MedicationSchedules",
                newName: "Frequency");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteAndUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExerciseDefinitions_Name",
                table: "ExerciseDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_DailyLogs_Date",
                table: "DailyLogs");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "WorkoutSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WorkoutSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "WorkoutRoutines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WorkoutRoutines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "WeeklyReviews",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WeeklyReviews",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "SleepEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "SleepEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "RoutineExercises",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "RoutineExercises",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ReminderSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ReminderSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "RecoveryEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "RecoveryEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "PhysicalWorkloadEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PhysicalWorkloadEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "NoteEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "NoteEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "MedicationSchedules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MedicationSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "MedicationEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MedicationEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "MealEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MealEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "LabResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "LabResults",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ExerciseSets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ExerciseSets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ExerciseFeedback",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ExerciseFeedback",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ExerciseDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ExerciseDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "DrinkEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DrinkEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "DailyLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DailyLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CessationGoals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CessationGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CessationEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CessationEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "BodyMeasurements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "BodyMeasurements",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseDefinitions_Name",
                table: "ExerciseDefinitions",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DailyLogs_Date",
                table: "DailyLogs",
                column: "Date",
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExerciseDefinitions_Name",
                table: "ExerciseDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_DailyLogs_Date",
                table: "DailyLogs");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WorkoutSessions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "WorkoutSessions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WorkoutRoutines");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "WorkoutRoutines");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WeeklyReviews");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "WeeklyReviews");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "SleepEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "SleepEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "RoutineExercises");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "RoutineExercises");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "RecoveryEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "RecoveryEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "PhysicalWorkloadEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PhysicalWorkloadEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "NoteEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "NoteEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MedicationEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MedicationEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MealEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ExerciseSets");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ExerciseSets");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ExerciseFeedback");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ExerciseFeedback");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ExerciseDefinitions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ExerciseDefinitions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DrinkEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DailyLogs");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DailyLogs");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CessationGoals");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CessationGoals");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CessationEvents");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CessationEvents");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "BodyMeasurements");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseDefinitions_Name",
                table: "ExerciseDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyLogs_Date",
                table: "DailyLogs",
                column: "Date",
                unique: true);
        }
    }
}

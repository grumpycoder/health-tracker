using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedExerciseLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RestSeconds",
                table: "ExerciseDefinitions");

            migrationBuilder.DropColumn(
                name: "TargetDurationSeconds",
                table: "ExerciseDefinitions");

            migrationBuilder.DropColumn(
                name: "TargetReps",
                table: "ExerciseDefinitions");

            migrationBuilder.DropColumn(
                name: "TargetSets",
                table: "ExerciseDefinitions");

            migrationBuilder.AddColumn<int>(
                name: "RestSeconds",
                table: "RoutineExercises",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetDurationSeconds",
                table: "RoutineExercises",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetReps",
                table: "RoutineExercises",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetSets",
                table: "RoutineExercises",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ExerciseDefinitions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseDefinitions_Name",
                table: "ExerciseDefinitions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExerciseDefinitions_Name",
                table: "ExerciseDefinitions");

            migrationBuilder.DropColumn(
                name: "RestSeconds",
                table: "RoutineExercises");

            migrationBuilder.DropColumn(
                name: "TargetDurationSeconds",
                table: "RoutineExercises");

            migrationBuilder.DropColumn(
                name: "TargetReps",
                table: "RoutineExercises");

            migrationBuilder.DropColumn(
                name: "TargetSets",
                table: "RoutineExercises");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ExerciseDefinitions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldCollation: "NOCASE");

            migrationBuilder.AddColumn<int>(
                name: "RestSeconds",
                table: "ExerciseDefinitions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetDurationSeconds",
                table: "ExerciseDefinitions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetReps",
                table: "ExerciseDefinitions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetSets",
                table: "ExerciseDefinitions",
                type: "INTEGER",
                nullable: true);
        }
    }
}

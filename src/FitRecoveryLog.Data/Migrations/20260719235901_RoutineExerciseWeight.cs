using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class RoutineExerciseWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetNote",
                table: "RoutineExercises",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TargetWeight",
                table: "RoutineExercises",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetNote",
                table: "RoutineExercises");

            migrationBuilder.DropColumn(
                name: "TargetWeight",
                table: "RoutineExercises");
        }
    }
}

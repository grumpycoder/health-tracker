using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BodyMeasurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WeightLbs = table.Column<double>(type: "REAL", nullable: true),
                    WaistInches = table.Column<double>(type: "REAL", nullable: true),
                    ChestInches = table.Column<double>(type: "REAL", nullable: true),
                    HipsInches = table.Column<double>(type: "REAL", nullable: true),
                    ArmsInches = table.Column<double>(type: "REAL", nullable: true),
                    ThighsInches = table.Column<double>(type: "REAL", nullable: true),
                    ClothingFitNotes = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoPath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodyMeasurements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DayType = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrinkEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Ounces = table.Column<double>(type: "REAL", nullable: true),
                    SugarCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrinkEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Measure = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetReps = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetSets = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    RestSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    EquipmentNotes = table.Column<string>(type: "TEXT", nullable: true),
                    ProgressionNotes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LabName = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MealType = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PortionNote = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    Satiety = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicationEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Dose = table.Column<string>(type: "TEXT", nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", nullable: true),
                    TakenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InjectionSite = table.Column<string>(type: "TEXT", nullable: true),
                    ReactionNotes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalWorkloadEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Activity = table.Column<string>(type: "TEXT", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    Intensity = table.Column<int>(type: "INTEGER", nullable: false),
                    BodyAreasAffected = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalWorkloadEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RecoveryRating = table.Column<int>(type: "INTEGER", nullable: true),
                    FatigueRating = table.Column<int>(type: "INTEGER", nullable: true),
                    SorenessLocations = table.Column<string>(type: "TEXT", nullable: true),
                    SorenessSeverity = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SleepEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DurationHours = table.Column<double>(type: "REAL", nullable: true),
                    SleepScore = table.Column<int>(type: "INTEGER", nullable: true),
                    Interruptions = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SleepEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WorkoutsCompleted = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AverageWorkoutMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    WeightChangeLbs = table.Column<double>(type: "REAL", nullable: true),
                    WaistChangeInches = table.Column<double>(type: "REAL", nullable: true),
                    BestPerformanceNote = table.Column<string>(type: "TEXT", nullable: true),
                    NutritionObservations = table.Column<string>(type: "TEXT", nullable: true),
                    SleepRecoveryObservations = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedFocus = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutRoutines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutRoutines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoutineExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoutineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineExercises_ExerciseDefinitions_ExerciseDefinitionId",
                        column: x => x.ExerciseDefinitionId,
                        principalTable: "ExerciseDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoutineExercises_WorkoutRoutines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "WorkoutRoutines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RoutineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutSessions_WorkoutRoutines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "WorkoutRoutines",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExerciseFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    PainOrDiscomfort = table.Column<bool>(type: "INTEGER", nullable: false),
                    BreathingDifficulty = table.Column<bool>(type: "INTEGER", nullable: false),
                    FormIssues = table.Column<bool>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseFeedback_ExerciseDefinitions_ExerciseDefinitionId",
                        column: x => x.ExerciseDefinitionId,
                        principalTable: "ExerciseDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseFeedback_WorkoutSessions_WorkoutSessionId",
                        column: x => x.WorkoutSessionId,
                        principalTable: "WorkoutSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SetNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Reps = table.Column<int>(type: "INTEGER", nullable: true),
                    Weight = table.Column<double>(type: "REAL", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    RestSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseSets_ExerciseDefinitions_ExerciseDefinitionId",
                        column: x => x.ExerciseDefinitionId,
                        principalTable: "ExerciseDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseSets_WorkoutSessions_WorkoutSessionId",
                        column: x => x.WorkoutSessionId,
                        principalTable: "WorkoutSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BodyMeasurements_Date",
                table: "BodyMeasurements",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_DailyLogs_Date",
                table: "DailyLogs",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseFeedback_ExerciseDefinitionId",
                table: "ExerciseFeedback",
                column: "ExerciseDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseFeedback_WorkoutSessionId",
                table: "ExerciseFeedback",
                column: "WorkoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseSets_ExerciseDefinitionId",
                table: "ExerciseSets",
                column: "ExerciseDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseSets_WorkoutSessionId",
                table: "ExerciseSets",
                column: "WorkoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineExercises_ExerciseDefinitionId",
                table: "RoutineExercises",
                column: "ExerciseDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineExercises_RoutineId",
                table: "RoutineExercises",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSessions_Date",
                table: "WorkoutSessions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSessions_RoutineId",
                table: "WorkoutSessions",
                column: "RoutineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BodyMeasurements");

            migrationBuilder.DropTable(
                name: "DailyLogs");

            migrationBuilder.DropTable(
                name: "DrinkEntries");

            migrationBuilder.DropTable(
                name: "ExerciseFeedback");

            migrationBuilder.DropTable(
                name: "ExerciseSets");

            migrationBuilder.DropTable(
                name: "LabResults");

            migrationBuilder.DropTable(
                name: "MealEntries");

            migrationBuilder.DropTable(
                name: "MedicationEntries");

            migrationBuilder.DropTable(
                name: "PhysicalWorkloadEntries");

            migrationBuilder.DropTable(
                name: "RecoveryEntries");

            migrationBuilder.DropTable(
                name: "RoutineExercises");

            migrationBuilder.DropTable(
                name: "SleepEntries");

            migrationBuilder.DropTable(
                name: "WeeklyReviews");

            migrationBuilder.DropTable(
                name: "WorkoutSessions");

            migrationBuilder.DropTable(
                name: "ExerciseDefinitions");

            migrationBuilder.DropTable(
                name: "WorkoutRoutines");
        }
    }
}

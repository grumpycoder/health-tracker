using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Server.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    WeightLbs = table.Column<double>(type: "float", nullable: true),
                    WaistInches = table.Column<double>(type: "float", nullable: true),
                    ChestInches = table.Column<double>(type: "float", nullable: true),
                    HipsInches = table.Column<double>(type: "float", nullable: true),
                    ArmsInches = table.Column<double>(type: "float", nullable: true),
                    ThighsInches = table.Column<double>(type: "float", nullable: true),
                    BodyFatPercent = table.Column<double>(type: "float", nullable: true),
                    MuscleMassLbs = table.Column<double>(type: "float", nullable: true),
                    VisceralFat = table.Column<double>(type: "float", nullable: true),
                    BodyWaterPercent = table.Column<double>(type: "float", nullable: true),
                    BasalMetabolicRate = table.Column<int>(type: "int", nullable: true),
                    MetabolicAge = table.Column<int>(type: "int", nullable: true),
                    ClothingFitNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhotoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodyMeasurements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CessationGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Substance = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Taper = table.Column<bool>(type: "bit", nullable: false),
                    TaperStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineUnitsPerDay = table.Column<double>(type: "float", nullable: true),
                    CostPerUnit = table.Column<double>(type: "float", nullable: true),
                    UnitName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PackName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UnitsPerPack = table.Column<double>(type: "float", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    FocusTrigger = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CessationGoals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DayType = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WaterOz = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrinkEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ounces = table.Column<double>(type: "float", nullable: true),
                    SugarCount = table.Column<int>(type: "int", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Calories = table.Column<int>(type: "int", nullable: true),
                    ProteinG = table.Column<double>(type: "float", nullable: true),
                    CarbsG = table.Column<double>(type: "float", nullable: true),
                    SugarG = table.Column<double>(type: "float", nullable: true),
                    FatG = table.Column<double>(type: "float", nullable: true),
                    SodiumMg = table.Column<int>(type: "int", nullable: true),
                    FiberG = table.Column<double>(type: "float", nullable: true),
                    AddedSugarG = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrinkEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Measure = table.Column<int>(type: "int", nullable: false),
                    EquipmentNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProgressionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VideoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MuscleGroups = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Retired = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    LabName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MealType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PortionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Satiety = table.Column<int>(type: "int", nullable: false),
                    QualityStars = table.Column<int>(type: "int", nullable: true),
                    Calories = table.Column<int>(type: "int", nullable: true),
                    ProteinG = table.Column<double>(type: "float", nullable: true),
                    CarbsG = table.Column<double>(type: "float", nullable: true),
                    SugarG = table.Column<double>(type: "float", nullable: true),
                    FatG = table.Column<double>(type: "float", nullable: true),
                    SodiumMg = table.Column<int>(type: "int", nullable: true),
                    FiberG = table.Column<double>(type: "float", nullable: true),
                    AddedSugarG = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicationEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Dose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TakenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InjectionSite = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReactionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicationSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Dose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Form = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    Repeat = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReminderTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoteEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalWorkloadEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Activity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    Intensity = table.Column<int>(type: "int", nullable: false),
                    BodyAreasAffected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalWorkloadEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RecoveryRating = table.Column<int>(type: "int", nullable: true),
                    FatigueRating = table.Column<int>(type: "int", nullable: true),
                    SorenessLocations = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SorenessSeverity = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReminderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Repeat = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<TimeOnly>(type: "time", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SleepEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DurationHours = table.Column<double>(type: "float", nullable: true),
                    SleepScore = table.Column<int>(type: "int", nullable: true),
                    Interruptions = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScoreEstimated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SleepEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkoutsCompleted = table.Column<int>(type: "int", nullable: false),
                    RecoveryDays = table.Column<int>(type: "int", nullable: false),
                    AverageWorkoutMinutes = table.Column<int>(type: "int", nullable: true),
                    WeightChangeLbs = table.Column<double>(type: "float", nullable: true),
                    WaistChangeInches = table.Column<double>(type: "float", nullable: true),
                    BestPerformanceNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NutritionObservations = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SleepRecoveryObservations = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SuggestedFocus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutRoutines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutRoutines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CessationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Intensity = table.Column<int>(type: "int", nullable: true),
                    Trigger = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<double>(type: "float", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CessationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CessationEvents_CessationGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "CessationGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoutineExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoutineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    TargetSets = table.Column<int>(type: "int", nullable: true),
                    TargetReps = table.Column<int>(type: "int", nullable: true),
                    TargetDurationSeconds = table.Column<int>(type: "int", nullable: true),
                    RestSeconds = table.Column<int>(type: "int", nullable: true),
                    TargetWeight = table.Column<double>(type: "float", nullable: true),
                    TargetNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RoutineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalSeconds = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    PainOrDiscomfort = table.Column<bool>(type: "bit", nullable: false),
                    BreathingDifficulty = table.Column<bool>(type: "bit", nullable: false),
                    FormIssues = table.Column<bool>(type: "bit", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetNumber = table.Column<int>(type: "int", nullable: false),
                    Reps = table.Column<int>(type: "int", nullable: true),
                    Weight = table.Column<double>(type: "float", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    RestSeconds = table.Column<int>(type: "int", nullable: true),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                name: "IX_CessationEvents_GoalId_Time",
                table: "CessationEvents",
                columns: new[] { "GoalId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyLogs_Date",
                table: "DailyLogs",
                column: "Date",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseDefinitions_Name",
                table: "ExerciseDefinitions",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

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
                name: "CessationEvents");

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
                name: "MedicationSchedules");

            migrationBuilder.DropTable(
                name: "NoteEntries");

            migrationBuilder.DropTable(
                name: "PhysicalWorkloadEntries");

            migrationBuilder.DropTable(
                name: "RecoveryEntries");

            migrationBuilder.DropTable(
                name: "ReminderSettings");

            migrationBuilder.DropTable(
                name: "RoutineExercises");

            migrationBuilder.DropTable(
                name: "SleepEntries");

            migrationBuilder.DropTable(
                name: "WeeklyReviews");

            migrationBuilder.DropTable(
                name: "CessationGoals");

            migrationBuilder.DropTable(
                name: "WorkoutSessions");

            migrationBuilder.DropTable(
                name: "ExerciseDefinitions");

            migrationBuilder.DropTable(
                name: "WorkoutRoutines");
        }
    }
}

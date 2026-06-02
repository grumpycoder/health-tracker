using System.ComponentModel.DataAnnotations.Schema;

namespace FitRecoveryLog.Data;

/// <summary>
/// Base for all records. Uses a GUID key so entries can be created offline and
/// merged later without collisions if we ever add sync.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>One row per calendar day: the planned day type and a freeform note.</summary>
public class DailyLog : EntityBase
{
    public DateOnly Date { get; set; }
    public DayType DayType { get; set; } = DayType.Unset;
    public string? Note { get; set; }
}

// ---------------------------------------------------------------------------
// Workouts
// ---------------------------------------------------------------------------

/// <summary>A reusable, named definition of an exercise (e.g. "Incline push-ups").</summary>
public class ExerciseDefinition : EntityBase
{
    public string Name { get; set; } = "";
    /// <summary>Whether this exercise is tracked by reps or by time held (e.g. planks).</summary>
    public ExerciseMeasure Measure { get; set; } = ExerciseMeasure.Reps;
    public int? TargetReps { get; set; }
    public int? TargetSets { get; set; }
    public int? TargetDurationSeconds { get; set; }
    public int? RestSeconds { get; set; }
    public string? EquipmentNotes { get; set; }
    public string? ProgressionNotes { get; set; }
}

/// <summary>A reusable routine: an ordered list of exercises.</summary>
public class WorkoutRoutine : EntityBase
{
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public List<RoutineExercise> Exercises { get; set; } = new();
}

/// <summary>Join row placing an ExerciseDefinition into a WorkoutRoutine at an order.</summary>
public class RoutineExercise : EntityBase
{
    public Guid RoutineId { get; set; }
    public WorkoutRoutine? Routine { get; set; }

    public Guid ExerciseDefinitionId { get; set; }
    public ExerciseDefinition? ExerciseDefinition { get; set; }

    public int Order { get; set; }
}

/// <summary>A performed workout, from start tap to finish.</summary>
public class WorkoutSession : EntityBase
{
    public DateOnly Date { get; set; }
    public Guid? RoutineId { get; set; }
    public WorkoutRoutine? Routine { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>Total elapsed time; auto-saved from start/end but manually adjustable.</summary>
    public int? TotalSeconds { get; set; }
    public string? Notes { get; set; }

    public List<ExerciseSet> Sets { get; set; } = new();
    public List<ExerciseFeedback> Feedback { get; set; } = new();
}

/// <summary>A single set performed during a session.</summary>
public class ExerciseSet : EntityBase
{
    public Guid WorkoutSessionId { get; set; }
    public WorkoutSession? WorkoutSession { get; set; }

    public Guid ExerciseDefinitionId { get; set; }
    public ExerciseDefinition? ExerciseDefinition { get; set; }

    public int SetNumber { get; set; }
    public int? Reps { get; set; }
    public double? Weight { get; set; }
    public int? DurationSeconds { get; set; }
    public int? RestSeconds { get; set; }
    public bool Completed { get; set; }
}

/// <summary>Post-workout feedback for one exercise in a session.</summary>
public class ExerciseFeedback : EntityBase
{
    public Guid WorkoutSessionId { get; set; }
    public WorkoutSession? WorkoutSession { get; set; }

    public Guid ExerciseDefinitionId { get; set; }
    public ExerciseDefinition? ExerciseDefinition { get; set; }

    public Difficulty Difficulty { get; set; } = Difficulty.Unset;
    public bool PainOrDiscomfort { get; set; }
    public bool BreathingDifficulty { get; set; }
    public bool FormIssues { get; set; }
    public string? Comment { get; set; }
}

// ---------------------------------------------------------------------------
// Nutrition
// ---------------------------------------------------------------------------

public class MealEntry : EntityBase
{
    public DateTime Time { get; set; } = DateTime.Now;
    public MealType MealType { get; set; } = MealType.Snack;
    public string Description { get; set; } = "";
    public string? PortionNote { get; set; }

    /// <summary>CSV of tags (High protein, Restaurant meal, etc.). Use <see cref="TagList"/>.</summary>
    public string? Tags { get; set; }
    public Satiety Satiety { get; set; } = Satiety.Unset;

    [NotMapped]
    public IReadOnlyList<string> TagList => CsvField.Split(Tags);
}

/// <summary>Drinks tracked separately so we can total tea/coffee/soda intake easily.</summary>
public class DrinkEntry : EntityBase
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
    public double? Ounces { get; set; }
    /// <summary>For coffee: number of sugar cubes/teaspoons added.</summary>
    public int? SugarCount { get; set; }
    public string? Tags { get; set; }

    [NotMapped]
    public IReadOnlyList<string> TagList => CsvField.Split(Tags);
}

// ---------------------------------------------------------------------------
// Body / recovery
// ---------------------------------------------------------------------------

public class BodyMeasurement : EntityBase
{
    public DateOnly Date { get; set; }
    public double? WeightLbs { get; set; }
    public double? WaistInches { get; set; }
    public double? ChestInches { get; set; }
    public double? HipsInches { get; set; }
    public double? ArmsInches { get; set; }
    public double? ThighsInches { get; set; }
    public string? ClothingFitNotes { get; set; }
    /// <summary>Optional on-device path to a progress photo.</summary>
    public string? PhotoPath { get; set; }
}

public class SleepEntry : EntityBase
{
    public DateOnly Date { get; set; }
    public double? DurationHours { get; set; }
    public int? SleepScore { get; set; }
    public int? Interruptions { get; set; }
    public string? Notes { get; set; }
}

public class RecoveryEntry : EntityBase
{
    public DateOnly Date { get; set; }
    /// <summary>1-10 subjective recovery rating.</summary>
    public int? RecoveryRating { get; set; }
    /// <summary>1-10 subjective fatigue rating.</summary>
    public int? FatigueRating { get; set; }

    /// <summary>CSV of soreness locations. Use <see cref="SorenessLocationList"/>.</summary>
    public string? SorenessLocations { get; set; }
    public SorenessSeverity SorenessSeverity { get; set; } = SorenessSeverity.None;
    public string? Notes { get; set; }

    [NotMapped]
    public IReadOnlyList<string> SorenessLocationList => CsvField.Split(SorenessLocations);
}

public class PhysicalWorkloadEntry : EntityBase
{
    public DateOnly Date { get; set; }
    /// <summary>Yard work, grass cutting, dog care, travel, etc.</summary>
    public string Activity { get; set; } = "";
    public int? DurationMinutes { get; set; }
    public Intensity Intensity { get; set; } = Intensity.Moderate;
    public string? BodyAreasAffected { get; set; }
    public string? Notes { get; set; }

    [NotMapped]
    public IReadOnlyList<string> BodyAreaList => CsvField.Split(BodyAreasAffected);
}

// ---------------------------------------------------------------------------
// Medication / labs
// ---------------------------------------------------------------------------

public class MedicationEntry : EntityBase
{
    public string Name { get; set; } = "";
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.Now;
    /// <summary>For injections (e.g. TRT): the site used.</summary>
    public string? InjectionSite { get; set; }
    public string? ReactionNotes { get; set; }
}

public class LabResult : EntityBase
{
    public DateOnly Date { get; set; }
    /// <summary>Testosterone, Hematocrit, PSA, A1C, etc. — freeform so custom labs work.</summary>
    public string LabName { get; set; } = "";
    public double? Value { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
}

// ---------------------------------------------------------------------------
// Weekly review
// ---------------------------------------------------------------------------

public class WeeklyReview : EntityBase
{
    public DateOnly WeekStart { get; set; }
    public int WorkoutsCompleted { get; set; }
    public int RecoveryDays { get; set; }
    public int? AverageWorkoutMinutes { get; set; }
    public double? WeightChangeLbs { get; set; }
    public double? WaistChangeInches { get; set; }
    public string? BestPerformanceNote { get; set; }
    public string? NutritionObservations { get; set; }
    public string? SleepRecoveryObservations { get; set; }
    public string? SuggestedFocus { get; set; }
}

/// <summary>Helpers for CSV-backed multi-value string columns.</summary>
public static class CsvField
{
    public static IReadOnlyList<string> Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Join(IEnumerable<string> values) => string.Join(",", values);
}

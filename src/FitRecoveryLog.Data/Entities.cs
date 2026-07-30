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

/// <summary>One row per calendar day: the planned day type and a freeform note.
/// (Note is legacy — freeform notes now live in <see cref="NoteEntry"/>.)</summary>
public class DailyLog : EntityBase
{
    public DateOnly Date { get; set; }
    public DayType DayType { get; set; } = DayType.Unset;
    public string? Note { get; set; }
    /// <summary>Water/fluids logged for the day, in fluid ounces (quick-add taps).</summary>
    public int WaterOz { get; set; }
}

/// <summary>A timestamped freeform note, logged any time of day
/// ("not hungry at normal lunch time", "felt great after workout").</summary>
public class NoteEntry : EntityBase
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Text { get; set; } = "";
}

// ---------------------------------------------------------------------------
// Workouts
// ---------------------------------------------------------------------------

/// <summary>
/// A library exercise — the canonical, reusable identity of an exercise
/// (e.g. "Incline push-ups"). Shared across routines so history and progression
/// accumulate. Per-routine targets live on <see cref="RoutineExercise"/>.
/// </summary>
public class ExerciseDefinition : EntityBase
{
    public string Name { get; set; } = "";
    /// <summary>Whether this exercise is tracked by reps or by time held (e.g. planks).</summary>
    public ExerciseMeasure Measure { get; set; } = ExerciseMeasure.Reps;
    public string? EquipmentNotes { get; set; }
    public string? ProgressionNotes { get; set; }
    /// <summary>Optional demo/how-to link (e.g. a YouTube video).</summary>
    public string? VideoUrl { get; set; }
    /// <summary>CSV of primary muscle groups worked (Chest, Back, …). Powers
    /// per-muscle volume analysis and routine balancing. Use <see cref="MuscleGroupList"/>.</summary>
    public string? MuscleGroups { get; set; }
    /// <summary>Hidden from routine pickers but kept for history.</summary>
    public bool Retired { get; set; }

    [NotMapped]
    public IReadOnlyList<string> MuscleGroupList => CsvField.Split(MuscleGroups);
}

/// <summary>A reusable routine: an ordered list of exercises.</summary>
public class WorkoutRoutine : EntityBase
{
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public List<RoutineExercise> Exercises { get; set; } = new();
}

/// <summary>
/// Places a library exercise into a routine at an order, with this routine's own
/// prescription (sets/reps/time/rest). The same exercise can be prescribed
/// differently in different routines.
/// </summary>
public class RoutineExercise : EntityBase
{
    public Guid RoutineId { get; set; }
    public WorkoutRoutine? Routine { get; set; }

    public Guid ExerciseDefinitionId { get; set; }
    public ExerciseDefinition? ExerciseDefinition { get; set; }

    public int Order { get; set; }

    // Per-routine prescription (moved off ExerciseDefinition).
    public int? TargetSets { get; set; }
    public int? TargetReps { get; set; }
    public int? TargetDurationSeconds { get; set; }
    public int? RestSeconds { get; set; }
    /// <summary>Target working weight in lb (per hand for dumbbell moves); null = bodyweight.</summary>
    public double? TargetWeight { get; set; }
    /// <summary>Free-text prescription nuance the numeric targets can't hold —
    /// rep ranges ("10-12"), "each arm/side/leg", "AMRAP", progression cues.</summary>
    public string? TargetNote { get; set; }
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
    /// <summary>Optional 1-5 "fit with your goals" score from the ✨ tag suggester.</summary>
    public int? QualityStars { get; set; }

    // Macros for the whole meal as eaten (label per-serving × servings eaten).
    // Populated by the nutrition-label scan or entered by hand; all optional.
    public int? Calories { get; set; }
    public double? ProteinG { get; set; }
    public double? CarbsG { get; set; }
    public double? SugarG { get; set; }
    public double? FatG { get; set; }
    public int? SodiumMg { get; set; }
    public double? FiberG { get; set; }
    /// <summary>Added sugars (label line), distinct from total SugarG.</summary>
    public double? AddedSugarG { get; set; }

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

    // Macros as consumed (from a scanned drink label, × servings); all optional.
    // Packaged drinks (protein shakes, juice, soda) carry real macros; coffee with
    // added sugar keeps using SugarCount instead.
    public int? Calories { get; set; }
    public double? ProteinG { get; set; }
    public double? CarbsG { get; set; }
    public double? SugarG { get; set; }
    public double? FatG { get; set; }
    public int? SodiumMg { get; set; }
    public double? FiberG { get; set; }
    /// <summary>Added sugars (label line); coffee's SugarCount also counts as added sugar.</summary>
    public double? AddedSugarG { get; set; }

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

    // Body-composition metrics from a smart scale (e.g. Hume). These don't sync to
    // Apple Health — Hume keeps them in-app — so they're entered manually.
    public double? BodyFatPercent { get; set; }
    public double? MuscleMassLbs { get; set; }
    public double? VisceralFat { get; set; }
    public double? BodyWaterPercent { get; set; }
    public int? BasalMetabolicRate { get; set; }
    public int? MetabolicAge { get; set; }

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
    /// <summary>True when SleepScore is our heuristic estimate rather than a real
    /// score (e.g. Apple's Sleep Score). Apple's score isn't exposed to HealthKit,
    /// so imports fill an estimate; entering the real score clears this.</summary>
    public bool ScoreEstimated { get; set; }
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

/// <summary>
/// A recurring medication the user can log with one tap (e.g. weekly TRT,
/// daily vitamin). Logging copies these defaults into a <see cref="MedicationEntry"/>.
/// </summary>
/// <summary>How a medication is taken. Injection drives the site prompt.</summary>
public enum MedicationForm { Oral = 0, Injection = 1, Topical = 2 }

public class MedicationSchedule : EntityBase
{
    public string Name { get; set; } = "";
    public string? Dose { get; set; }
    /// <summary>How it's taken; Injection prompts for a site (with rotation suggestion).</summary>
    public MedicationForm Form { get; set; } = MedicationForm.Oral;
    /// <summary>Back-compat shim over <see cref="Form"/> — kept so pre-Form JSON backups
    /// restore the injection flag. Not a column.</summary>
    [NotMapped]
    public bool IsInjection
    {
        get => Form == MedicationForm.Injection;
        set
        {
            if (value) Form = MedicationForm.Injection;
            else if (Form == MedicationForm.Injection) Form = MedicationForm.Oral;
        }
    }
    public bool Active { get; set; } = true;

    // Schedule → reminders. While active and within [StartDate, EndDate], this drives
    // a recurring reminder; completing that reminder logs a dose.
    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.Daily;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public TimeOnly ReminderTime { get; set; } = new(9, 0);
    /// <summary>Stable id used to schedule/cancel this schedule's OS notification.</summary>
    public int NotificationId { get; set; }
}

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

/// <summary>
/// A habit being quit (tobacco, alcohol, …). Streaks are derived, never stored:
/// the quit date never resets; "days since last slip" comes from events — a slip
/// does not erase progress (non-judgmental by design).
/// </summary>
public class CessationGoal : EntityBase
{
    public string Substance { get; set; } = "";
    public DateOnly QuitDate { get; set; }
    /// <summary>False = cold turkey (QuitDate is the day you stopped); true = tapering
    /// (QuitDate is the TARGET — daily allowance falls linearly from the baseline to 0
    /// between <see cref="TaperStartDate"/> and QuitDate).</summary>
    public bool Taper { get; set; }
    /// <summary>When the taper began (set on goal creation for taper goals).</summary>
    public DateOnly? TaperStartDate { get; set; }
    /// <summary>Pre-quit usage in counting units (e.g. 15 cigarettes/day).</summary>
    public double? BaselineUnitsPerDay { get; set; }
    /// <summary>Cost per purchase unit: per pack when <see cref="UnitsPerPack"/> is set,
    /// otherwise per counting unit.</summary>
    public double? CostPerUnit { get; set; }
    /// <summary>Counting unit — what gets logged and tapered ("cigarette", "drink").</summary>
    public string? UnitName { get; set; }
    /// <summary>Optional purchase unit ("pack", "carton") when cost isn't per counting unit.</summary>
    public string? PackName { get; set; }
    /// <summary>Counting units per purchase unit (e.g. 20 cigarettes per pack).</summary>
    public double? UnitsPerPack { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Stable id for milestone notifications.</summary>
    public int NotificationId { get; set; }
    /// <summary>The one trigger the user is actively trying to break, if any.</summary>
    public string? FocusTrigger { get; set; }
}

public enum CessationEventType { Craving = 0, Slip = 1 }

/// <summary>A craving ridden out or a slip — both are data, not failure.</summary>
public class CessationEvent : EntityBase
{
    public Guid GoalId { get; set; }
    public CessationGoal? Goal { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public CessationEventType Type { get; set; }
    /// <summary>1 (mild) – 5 (intense).</summary>
    public int? Intensity { get; set; }
    /// <summary>What set it off (stress, social, boredom, …).</summary>
    public string? Trigger { get; set; }
    /// <summary>Units consumed, for slips.</summary>
    public double? Amount { get; set; }
    public string? Notes { get; set; }
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

/// <summary>
/// Cadence config for a built-in derived reminder (body measurement, lab check,
/// weekly review). Reminders aren't hand-created — they're generated from these
/// settings and from medication schedules.
/// </summary>
public class ReminderSetting : EntityBase
{
    /// <summary>Stable key: "measurement", "labCheck", "weeklyReview".</summary>
    public string Key { get; set; } = "";
    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.Weekly;
    public TimeOnly Time { get; set; } = new(9, 0);
    public bool Active { get; set; }
    /// <summary>Stable id used to schedule/cancel the OS notification.</summary>
    public int NotificationId { get; set; }
    /// <summary>For weekly reminders: which day it fires (0=Sunday..6=Saturday).
    /// Null is treated as Sunday, so a weekly reminder fires once on a fixed day
    /// instead of floating to whatever weekday the app was last opened.</summary>
    public int? DayOfWeek { get; set; }
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

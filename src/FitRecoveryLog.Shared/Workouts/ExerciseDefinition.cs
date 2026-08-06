using System.ComponentModel.DataAnnotations.Schema;

namespace FitRecoveryLog.Data;

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

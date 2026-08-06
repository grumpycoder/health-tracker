namespace FitRecoveryLog.Data;

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

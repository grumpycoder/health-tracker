namespace FitRecoveryLog.Data;

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

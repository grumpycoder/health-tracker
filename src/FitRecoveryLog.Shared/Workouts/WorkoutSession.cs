namespace FitRecoveryLog.Data;

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

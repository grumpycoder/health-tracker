namespace FitRecoveryLog.Data;

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

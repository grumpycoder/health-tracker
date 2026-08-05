namespace FitRecoveryLog.Domain.Workouts;

/// <summary>Per-exercise feedback captured after a workout (difficulty + flags + comment).
/// Local-identity entity within a <see cref="WorkoutSession"/>; one per exercise.</summary>
public sealed class WorkoutFeedback
{
    public Guid Id { get; }
    public Guid ExerciseDefinitionId { get; }
    public Difficulty Difficulty { get; private set; }
    public bool PainOrDiscomfort { get; private set; }
    public bool BreathingDifficulty { get; private set; }
    public bool FormIssues { get; private set; }
    public string? Comment { get; private set; }

    internal WorkoutFeedback(Guid id, Guid exerciseDefinitionId, Difficulty difficulty,
        bool painOrDiscomfort, bool breathingDifficulty, bool formIssues, string? comment)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException("An exercise definition is required.", nameof(exerciseDefinitionId));
        Id = id;
        ExerciseDefinitionId = exerciseDefinitionId;
        Update(difficulty, painOrDiscomfort, breathingDifficulty, formIssues, comment);
    }

    public static WorkoutFeedback Rehydrate(Guid id, Guid exerciseDefinitionId, Difficulty difficulty,
        bool pain, bool breathing, bool form, string? comment) =>
        new(id, exerciseDefinitionId, difficulty, pain, breathing, form, comment);

    internal void Update(Difficulty difficulty, bool pain, bool breathing, bool form, string? comment)
    {
        Difficulty = difficulty;
        PainOrDiscomfort = pain;
        BreathingDifficulty = breathing;
        FormIssues = form;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }
}

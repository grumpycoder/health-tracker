namespace FitRecoveryLog.Domain.Workouts;

/// <summary>
/// A logged workout: when it happened, its sets, and per-exercise feedback. Aggregate root —
/// sets and feedback are only changed through it, so it can keep its invariants: set numbers
/// are contiguous per exercise (1..N), and each exercise has at most one feedback entry.
/// May be linked to the routine it was run from (nullable, and cleared if that routine is deleted).
/// </summary>
public sealed class WorkoutSession
{
    private readonly List<WorkoutSet> _sets;
    private readonly List<WorkoutFeedback> _feedback;

    public Guid Id { get; }
    public DateOnly Date { get; private set; }
    public Guid? RoutineId { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public int? TotalSeconds { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyList<WorkoutSet> Sets =>
        _sets.OrderBy(s => s.ExerciseDefinitionId).ThenBy(s => s.SetNumber).ToList();
    public IReadOnlyList<WorkoutFeedback> Feedback => _feedback.ToList();

    private WorkoutSession(Guid id, DateOnly date, Guid? routineId, DateTime? startedAt, DateTime? endedAt,
        int? totalSeconds, string? notes, List<WorkoutSet> sets, List<WorkoutFeedback> feedback)
    {
        Id = id; Date = date; RoutineId = routineId; StartedAt = startedAt; EndedAt = endedAt;
        TotalSeconds = totalSeconds; Notes = notes; _sets = sets; _feedback = feedback;
    }

    public static WorkoutSession Create(DateOnly date, Guid? routineId = null) =>
        new(Guid.NewGuid(), date, routineId, null, null, null, null, new(), new());

    public static WorkoutSession Rehydrate(Guid id, DateOnly date, Guid? routineId, DateTime? startedAt,
        DateTime? endedAt, int? totalSeconds, string? notes,
        IEnumerable<WorkoutSet> sets, IEnumerable<WorkoutFeedback> feedback)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        var session = new WorkoutSession(id, date, routineId, startedAt, endedAt, totalSeconds,
            string.IsNullOrWhiteSpace(notes) ? null : notes, sets.ToList(), feedback.ToList());
        foreach (var group in session._sets.GroupBy(s => s.ExerciseDefinitionId))
            Renumber(group.ToList());
        return session;
    }

    public void SetDate(DateOnly date) => Date = date;
    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    public void SetDurationSeconds(int? seconds)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        TotalSeconds = seconds;
    }

    public void Start(DateTime startedAt) => StartedAt = startedAt;

    /// <summary>Mark the workout finished; derives duration from start when not set explicitly.</summary>
    public void Finish(DateTime endedAt)
    {
        if (StartedAt is DateTime s && endedAt < s)
            throw new ArgumentException("End cannot precede start.", nameof(endedAt));
        EndedAt = endedAt;
        TotalSeconds ??= StartedAt is DateTime start ? (int)(endedAt - start).TotalSeconds : null;
    }

    // ---- sets ----

    public Guid AddSet(Guid exerciseDefinitionId, SetResult result, bool completed = false)
    {
        var next = _sets.Where(s => s.ExerciseDefinitionId == exerciseDefinitionId)
                        .Select(s => s.SetNumber).DefaultIfEmpty(0).Max() + 1;
        var set = new WorkoutSet(Guid.NewGuid(), exerciseDefinitionId, next, result, completed);
        _sets.Add(set);
        return set.Id;
    }

    public void UpdateSet(Guid setId, SetResult result, bool completed) => FindSet(setId).Update(result, completed);

    public void RemoveSet(Guid setId)
    {
        var set = FindSet(setId);
        _sets.Remove(set);
        Renumber(_sets.Where(s => s.ExerciseDefinitionId == set.ExerciseDefinitionId).ToList());
    }

    // ---- feedback (one per exercise) ----

    public Guid SetFeedback(Guid exerciseDefinitionId, Difficulty difficulty,
        bool pain, bool breathing, bool form, string? comment)
    {
        var existing = _feedback.FirstOrDefault(f => f.ExerciseDefinitionId == exerciseDefinitionId);
        if (existing is not null)
        {
            existing.Update(difficulty, pain, breathing, form, comment);
            return existing.Id;
        }
        var fb = new WorkoutFeedback(Guid.NewGuid(), exerciseDefinitionId, difficulty, pain, breathing, form, comment);
        _feedback.Add(fb);
        return fb.Id;
    }

    public void RemoveFeedback(Guid feedbackId)
    {
        var fb = _feedback.FirstOrDefault(f => f.Id == feedbackId)
            ?? throw new InvalidOperationException("Feedback is not part of this session.");
        _feedback.Remove(fb);
    }

    private WorkoutSet FindSet(Guid setId) =>
        _sets.FirstOrDefault(s => s.Id == setId)
        ?? throw new InvalidOperationException("Set is not part of this session.");

    private static void Renumber(List<WorkoutSet> exerciseSets)
    {
        var ordered = exerciseSets.OrderBy(s => s.SetNumber).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].SetNumberTo(i + 1);
    }
}

using FitRecoveryLog.Data;

namespace FitRecoveryLog.Services;

public enum WorkoutState { Choosing, Active, Feedback }

public sealed class SetVM { public int SetNumber; public int? Reps; public double? Weight; public int? DurationSeconds; public bool Completed; }

public sealed class ExVM
{
    public ExerciseDefinition Def = null!;
    public int? Rest;    // this routine's rest prescription
    public string? TargetNote; // this routine's rep-range / per-side / progression cue
    public bool Skipped; // skipped for this session (injury etc.)
    public List<SetVM> Sets = new();
    public ExerciseFeedback Feedback = new();
}

public sealed class StepVM { public ExVM Ex = null!; public SetVM Set = null!; }

/// <summary>The mutable state of one in-progress workout.</summary>
public sealed class WorkoutRun
{
    public WorkoutState State = WorkoutState.Choosing;
    public List<ExVM> Exercises = new();
    public List<StepVM> Steps = new();
    public int StepIndex;
    public Guid? RoutineId;
    public string RoutineName = "Workout";
    public DateTime StartedAt;
    public int Rest;
    public int TotalMinutes;
    public string? SessionNotes;
    // Live hold countdown for a timed set.
    public SetVM? TimingSet;
    public SetVM? HoldDoneSet;
    public DateTime SetStart;
    public int HoldTarget;
}

/// <summary>
/// Singleton that holds the in-progress workout so it survives navigating away
/// from the Workout screen — Blazor disposes the page component on navigation,
/// which would otherwise lose the whole session.
/// </summary>
public sealed class ActiveWorkoutState
{
    public WorkoutRun? Current { get; set; }
    /// <summary>An active or finishing workout the user can return to.</summary>
    public bool HasResumable => Current is { State: WorkoutState.Active or WorkoutState.Feedback };
}

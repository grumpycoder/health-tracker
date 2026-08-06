namespace FitRecoveryLog.Application.Ai;

/// <summary>
/// Per-client read model for AI prompts: gathers the local data a prompt needs and formats it
/// into a context block. The phone reads EF/SQLite; the web will read its sync cache. This keeps
/// data access — including the non-migrated entities some prompts touch (daily logs, cessation,
/// physical workload) — out of the shared, provider-agnostic coach.
/// </summary>
public interface IAiDataProvider
{
    /// <summary>Today's context — plan, notes, sleep, today's workout, meals, drinks, running
    /// totals, physical workload, a 7-day rear-view, and (only when <paramref name="includeCessation"/>
    /// is true) cessation goals — rendered as prompt text.</summary>
    Task<string> GetTodayContextAsync(bool includeCessation, CancellationToken ct = default);

    /// <summary>The last-8-weeks data block for the full analysis — workouts (sets + feedback),
    /// meals, drinks (+ weekly volume), body measurements, and sleep — as prompt text.</summary>
    Task<string> GetEightWeekContextAsync(CancellationToken ct = default);

    /// <summary>The routine-design data block — exercise library, existing routines, per-exercise
    /// training history, and per-muscle set volume — as prompt text.</summary>
    Task<string> GetRoutineDesignContextAsync(CancellationToken ct = default);

    /// <summary>Exercises whose most recent difficulty rating in the window is "Easy" — a
    /// code-level backstop so a fresh routine can't reuse them.</summary>
    Task<IReadOnlyCollection<string>> RecentlyEasyExercisesAsync(CancellationToken ct = default);
}

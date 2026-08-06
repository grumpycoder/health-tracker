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
}

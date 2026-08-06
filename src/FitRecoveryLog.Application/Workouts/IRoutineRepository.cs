using FitRecoveryLog.Domain.Workouts;

namespace FitRecoveryLog.Application.Workouts;

/// <summary>
/// Persistence port for the <see cref="Routine"/> aggregate. Each client supplies its own
/// implementation — the phone against local SQLite, the web against the cloud sync API — so
/// the use cases in <see cref="RoutineService"/> run identically everywhere.
/// </summary>
public interface IRoutineRepository
{
    Task<Routine?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Routine>> ListAsync(bool includeArchived, CancellationToken ct = default);
    Task SaveAsync(Routine routine, CancellationToken ct = default);
    /// <summary>Soft-delete the routine and its exercises.</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

/// <summary>The session operations the routine use cases need to protect workout history: a
/// routine with any logged sessions is archived, never deleted, so we count them; the cascade
/// delete exists only for the explicit "delete the history too" path (test/cleanup).</summary>
public interface IWorkoutSessionRepository
{
    /// <summary>How many live (non-deleted) sessions reference this routine.</summary>
    Task<int> CountByRoutineAsync(Guid routineId, CancellationToken ct = default);
    /// <summary>Soft-delete every session of this routine. Invoked only when a caller
    /// explicitly opts into deleting the logged workouts along with the routine.</summary>
    Task DeleteByRoutineAsync(Guid routineId, CancellationToken ct = default);
}

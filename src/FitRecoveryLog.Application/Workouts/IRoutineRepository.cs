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

/// <summary>Just the session operation the routine use cases need — detaching a deleted
/// routine's past sessions so their history survives.</summary>
public interface IWorkoutSessionRepository
{
    Task DetachFromRoutineAsync(Guid routineId, CancellationToken ct = default);
}

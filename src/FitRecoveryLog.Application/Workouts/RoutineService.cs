using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Workouts;

namespace FitRecoveryLog.Application.Workouts;

/// <summary>
/// Application use cases for routines. Thin by design: it loads the aggregate, invokes its
/// behavior (which enforces the invariants), and persists — the rules live in
/// <see cref="Routine"/>, not here. Shared by every client so they all behave identically.
/// </summary>
public sealed class RoutineService
{
    private readonly IRoutineRepository _routines;
    private readonly IWorkoutSessionRepository _sessions;

    public RoutineService(IRoutineRepository routines, IWorkoutSessionRepository sessions)
    {
        _routines = routines;
        _sessions = sessions;
    }

    public Task<IReadOnlyList<Routine>> ListAsync(bool includeArchived, CancellationToken ct = default) =>
        _routines.ListAsync(includeArchived, ct);

    public async Task<Result<Guid>> CreateAsync(string name, string? notes = null, CancellationToken ct = default)
    {
        Routine routine;
        try { routine = Routine.Create(name, notes); }
        catch (ArgumentException ex) { return Result<Guid>.Failure(ex.Message); }
        await _routines.SaveAsync(routine, ct);
        return Result<Guid>.Success(routine.Id);
    }

    public Task<Result> RenameAsync(Guid id, string name, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Rename(name), ct);

    public Task<Result> SetNotesAsync(Guid id, string? notes, CancellationToken ct = default) =>
        MutateAsync(id, r => r.SetNotes(notes), ct);

    public Task<Result> ArchiveAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Archive(), ct);

    public Task<Result> RestoreAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Restore(), ct);

    public Task<Result> RemoveExerciseAsync(Guid id, Guid routineExerciseId, CancellationToken ct = default) =>
        MutateAsync(id, r => r.RemoveExercise(routineExerciseId), ct);

    public Task<Result> UpdateExerciseAsync(Guid id, Guid routineExerciseId, ExercisePrescription prescription, CancellationToken ct = default) =>
        MutateAsync(id, r => r.UpdateExercise(routineExerciseId, prescription), ct);

    public Task<Result> MoveExerciseAsync(Guid id, Guid routineExerciseId, int newPosition, CancellationToken ct = default) =>
        MutateAsync(id, r => r.MoveExercise(routineExerciseId, newPosition), ct);

    public async Task<Result<Guid>> AddExerciseAsync(Guid id, Guid exerciseDefinitionId, ExercisePrescription prescription, CancellationToken ct = default)
    {
        var routine = await _routines.GetAsync(id, ct);
        if (routine is null) return Result<Guid>.Failure("Routine not found.");
        Guid exerciseId;
        try { exerciseId = routine.AddExercise(exerciseDefinitionId, prescription); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Result<Guid>.Failure(ex.Message); }
        await _routines.SaveAsync(routine, ct);
        return Result<Guid>.Success(exerciseId);
    }

    /// <summary>
    /// Delete a routine. Refused when it has logged workouts — those are real history, so the
    /// routine should be <see cref="Routine.Archive">archived</see> instead. Deleting is only
    /// for routines with no history, or, when <paramref name="deleteSessions"/> is set, together
    /// with a cascade delete of the sessions (a test/cleanup affordance, never normal use).
    /// This cross-aggregate rule lives here because a <see cref="Routine"/> can't see its
    /// sessions; it's an invariant, not a domain event.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, bool deleteSessions = false, CancellationToken ct = default)
    {
        var routine = await _routines.GetAsync(id, ct);
        if (routine is null) return Result.Failure("Routine not found.");

        var sessionCount = await _sessions.CountByRoutineAsync(id, ct);
        if (sessionCount > 0 && !deleteSessions)
            return Result.Failure(
                $"This routine has {sessionCount} logged workout{(sessionCount == 1 ? "" : "s")}. " +
                "Archive it to keep that history, or delete those workouts first.");

        if (sessionCount > 0)
            await _sessions.DeleteByRoutineAsync(id, ct);

        await _routines.RemoveAsync(id, ct);
        return Result.Success();
    }

    private async Task<Result> MutateAsync(Guid id, Action<Routine> change, CancellationToken ct)
    {
        var routine = await _routines.GetAsync(id, ct);
        if (routine is null) return Result.Failure("Routine not found.");
        try { change(routine); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Result.Failure(ex.Message);
        }
        await _routines.SaveAsync(routine, ct);
        return Result.Success();
    }
}

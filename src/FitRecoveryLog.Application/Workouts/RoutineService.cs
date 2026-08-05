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

    /// <summary>Delete a routine, first detaching its past sessions so their history survives.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var routine = await _routines.GetAsync(id, ct);
        if (routine is null) return Result.Failure("Routine not found.");
        await _sessions.DetachFromRoutineAsync(id, ct);
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

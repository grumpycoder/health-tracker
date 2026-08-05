using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Workouts;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>
/// Web implementation of <see cref="IRoutineRepository"/>. The browser has no local database,
/// so it reads from the pulled cloud snapshot (<see cref="AppState"/>) and writes by pushing
/// rows through the sync API — the same port the phone implements over EF. This is what lets
/// the shared <see cref="RoutineService"/> use cases run identically on the web.
/// </summary>
public sealed class ApiRoutineRepository : IRoutineRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiRoutineRepository(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    public async Task<Routine?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.WorkoutRoutine>(pull).FirstOrDefault(r => r.Id == id);
        if (row is null) return null;
        var exercises = WebSyncClient.Rows<Persistence.RoutineExercise>(pull).Where(e => e.RoutineId == id);
        return ToDomain(row, exercises);
    }

    public async Task<IReadOnlyList<Routine>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var byRoutine = WebSyncClient.Rows<Persistence.RoutineExercise>(pull)
            .GroupBy(e => e.RoutineId).ToDictionary(g => g.Key, g => (IEnumerable<Persistence.RoutineExercise>)g.ToList());
        return WebSyncClient.Rows<Persistence.WorkoutRoutine>(pull)
            .Where(r => includeArchived || !r.Archived)
            .Select(r => ToDomain(r, byRoutine.GetValueOrDefault(r.Id, Array.Empty<Persistence.RoutineExercise>())))
            .ToList();
    }

    public async Task SaveAsync(Routine routine, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.WorkoutRoutine>(pull).FirstOrDefault(r => r.Id == routine.Id);

        await _sync.PushAsync(new Persistence.WorkoutRoutine
        {
            Id = routine.Id,
            Name = routine.Name,
            Notes = routine.Notes,
            Archived = routine.Archived,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });

        // Reconcile exercises: upsert the aggregate's, tombstone any that were removed.
        var stored = WebSyncClient.Rows<Persistence.RoutineExercise>(pull).Where(e => e.RoutineId == routine.Id).ToList();
        var keep = routine.Exercises.Select(e => e.Id).ToHashSet();

        var toPush = routine.Exercises.Select(e => new Persistence.RoutineExercise
        {
            Id = e.Id,
            RoutineId = routine.Id,
            ExerciseDefinitionId = e.ExerciseDefinitionId,
            Order = e.Order,
            TargetSets = e.Prescription.TargetSets,
            TargetReps = e.Prescription.TargetReps,
            TargetDurationSeconds = e.Prescription.TargetDurationSeconds,
            RestSeconds = e.Prescription.RestSeconds,
            TargetWeight = e.Prescription.TargetWeight,
            TargetNote = e.Prescription.TargetNote,
        }).ToList();

        foreach (var gone in stored.Where(s => !keep.Contains(s.Id)))
        {
            gone.IsDeleted = true;
            gone.DeletedAt = DateTime.UtcNow;
            toPush.Add(gone);
        }

        if (toPush.Count > 0) await _sync.PushAsync(toPush);
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.WorkoutRoutine>(pull).FirstOrDefault(r => r.Id == id);
        if (row is null) return;

        row.IsDeleted = true;
        row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);

        var exercises = WebSyncClient.Rows<Persistence.RoutineExercise>(pull).Where(e => e.RoutineId == id).ToList();
        foreach (var e in exercises) { e.IsDeleted = true; e.DeletedAt = DateTime.UtcNow; }
        if (exercises.Count > 0) await _sync.PushAsync(exercises);

        _state.Invalidate();
    }

    private static Routine ToDomain(Persistence.WorkoutRoutine row, IEnumerable<Persistence.RoutineExercise> exercises) =>
        Routine.Rehydrate(row.Id, row.Name, row.Notes, row.Archived,
            exercises.Select(e => FitRecoveryLog.Domain.Workouts.RoutineExercise.Rehydrate(
                e.Id, e.ExerciseDefinitionId, e.Order,
                new ExercisePrescription(e.TargetSets, e.TargetReps, e.TargetDurationSeconds,
                    e.RestSeconds, e.TargetWeight, e.TargetNote))));
}

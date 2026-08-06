using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Workouts;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Workouts;

/// <summary>
/// EF Core implementation of <see cref="IRoutineRepository"/>. Maps between the rich
/// <see cref="Routine"/> aggregate and the persistence entities (<see cref="Persistence.WorkoutRoutine"/>
/// + <see cref="Persistence.RoutineExercise"/>). The domain stays free of EF; this repository is the
/// only place that knows both. Soft-deletes and UTC stamping are handled by <see cref="AppDbContext"/>.
/// </summary>
public sealed class EfRoutineRepository : IRoutineRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfRoutineRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<Routine?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkoutRoutines.Include(r => r.Exercises)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Routine>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.WorkoutRoutines.Include(r => r.Exercises).AsQueryable();
        if (!includeArchived) query = query.Where(r => !r.Archived);
        var rows = await query.ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(Routine routine, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkoutRoutines.Include(r => r.Exercises)
            .FirstOrDefaultAsync(r => r.Id == routine.Id, ct);

        if (row is null)
        {
            row = new Persistence.WorkoutRoutine { Id = routine.Id };
            db.WorkoutRoutines.Add(row);
        }

        row.Name = routine.Name;
        row.Notes = routine.Notes;
        row.Archived = routine.Archived;

        // Reconcile the child collection to match the aggregate.
        var keep = routine.Exercises.Select(e => e.Id).ToHashSet();
        foreach (var removed in row.Exercises.Where(e => !keep.Contains(e.Id)).ToList())
            db.RoutineExercises.Remove(removed); // -> tombstone

        foreach (var e in routine.Exercises)
        {
            var pe = row.Exercises.FirstOrDefault(x => x.Id == e.Id);
            if (pe is null)
            {
                pe = new Persistence.RoutineExercise { Id = e.Id, RoutineId = routine.Id };
                row.Exercises.Add(pe);
                // The Id is a store-generated key already set to the aggregate's value. When the
                // parent is pre-existing (tracked as Unchanged), EF's graph heuristic would treat
                // this new child as an existing row and emit an UPDATE (0 rows affected). Force
                // Added so it INSERTs.
                db.Entry(pe).State = EntityState.Added;
            }
            pe.ExerciseDefinitionId = e.ExerciseDefinitionId;
            pe.Order = e.Order;
            pe.TargetSets = e.Prescription.TargetSets;
            pe.TargetReps = e.Prescription.TargetReps;
            pe.TargetDurationSeconds = e.Prescription.TargetDurationSeconds;
            pe.RestSeconds = e.Prescription.RestSeconds;
            pe.TargetWeight = e.Prescription.TargetWeight;
            pe.TargetNote = e.Prescription.TargetNote;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkoutRoutines.Include(r => r.Exercises)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return;

        foreach (var e in row.Exercises.ToList())
            db.RoutineExercises.Remove(e);
        db.WorkoutRoutines.Remove(row); // AppDbContext turns these into tombstones

        await db.SaveChangesAsync(ct);
    }

    private static Routine ToDomain(Persistence.WorkoutRoutine row) =>
        Routine.Rehydrate(row.Id, row.Name, row.Notes, row.Archived,
            row.Exercises.Select(e => FitRecoveryLog.Domain.Workouts.RoutineExercise.Rehydrate(
                e.Id, e.ExerciseDefinitionId, e.Order,
                new ExercisePrescription(e.TargetSets, e.TargetReps, e.TargetDurationSeconds,
                    e.RestSeconds, e.TargetWeight, e.TargetNote))));
}

using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Workouts;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Workouts;

/// <summary>
/// EF Core implementation of <see cref="IWorkoutRepository"/>. Maps the <see cref="WorkoutSession"/>
/// aggregate (session + sets + feedback) to the persistence entities, reconciling the child
/// collections on save. The domain <see cref="Difficulty"/> maps to the storage enum by ordinal.
/// The persistence types collide with the domain names, so they're referenced through the
/// <c>Persistence</c> alias (this file does not import <c>FitRecoveryLog.Data</c>).
/// </summary>
public sealed class EfWorkoutRepository : IWorkoutRepository
{
    private readonly IDbContextFactory<Persistence.AppDbContext> _factory;

    public EfWorkoutRepository(IDbContextFactory<Persistence.AppDbContext> factory) => _factory = factory;

    public async Task<WorkoutSession?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkoutSessions.Include(s => s.Sets).Include(s => s.Feedback)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<WorkoutSession>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.WorkoutSessions.Include(s => s.Sets).Include(s => s.Feedback)
            .OrderByDescending(s => s.Date).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(WorkoutSession session, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkoutSessions.Include(s => s.Sets).Include(s => s.Feedback)
            .FirstOrDefaultAsync(s => s.Id == session.Id, ct);
        if (row is null)
        {
            row = new Persistence.WorkoutSession { Id = session.Id };
            db.WorkoutSessions.Add(row);
        }

        row.Date = session.Date;
        row.RoutineId = session.RoutineId;
        row.StartedAt = session.StartedAt;
        row.EndedAt = session.EndedAt;
        row.TotalSeconds = session.TotalSeconds;
        row.Notes = session.Notes;

        ReconcileSets(db, row, session);
        ReconcileFeedback(db, row, session);

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkoutSessions.Include(s => s.Sets).Include(s => s.Feedback)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return;
        foreach (var s in row.Sets.ToList()) db.ExerciseSets.Remove(s);
        foreach (var f in row.Feedback.ToList()) db.ExerciseFeedback.Remove(f);
        db.WorkoutSessions.Remove(row); // AppDbContext converts these to tombstones
        await db.SaveChangesAsync(ct);
    }

    private static void ReconcileSets(Persistence.AppDbContext db, Persistence.WorkoutSession row, WorkoutSession session)
    {
        var keep = session.Sets.Select(s => s.Id).ToHashSet();
        foreach (var gone in row.Sets.Where(s => !keep.Contains(s.Id)).ToList())
            db.ExerciseSets.Remove(gone);

        foreach (var s in session.Sets)
        {
            var pe = row.Sets.FirstOrDefault(x => x.Id == s.Id);
            if (pe is null)
            {
                pe = new Persistence.ExerciseSet { Id = s.Id, WorkoutSessionId = session.Id };
                row.Sets.Add(pe);
                // Pre-set store-generated key on a tracked parent → force Added so EF INSERTs
                // (see EfRoutineRepository for the same footgun).
                db.Entry(pe).State = EntityState.Added;
            }
            pe.ExerciseDefinitionId = s.ExerciseDefinitionId;
            pe.SetNumber = s.SetNumber;
            pe.Reps = s.Result.Reps;
            pe.Weight = s.Result.Weight;
            pe.DurationSeconds = s.Result.DurationSeconds;
            pe.RestSeconds = s.Result.RestSeconds;
            pe.Completed = s.Completed;
        }
    }

    private static void ReconcileFeedback(Persistence.AppDbContext db, Persistence.WorkoutSession row, WorkoutSession session)
    {
        var keep = session.Feedback.Select(f => f.Id).ToHashSet();
        foreach (var gone in row.Feedback.Where(f => !keep.Contains(f.Id)).ToList())
            db.ExerciseFeedback.Remove(gone);

        foreach (var f in session.Feedback)
        {
            var pe = row.Feedback.FirstOrDefault(x => x.Id == f.Id);
            if (pe is null)
            {
                pe = new Persistence.ExerciseFeedback { Id = f.Id, WorkoutSessionId = session.Id };
                row.Feedback.Add(pe);
                db.Entry(pe).State = EntityState.Added;
            }
            pe.ExerciseDefinitionId = f.ExerciseDefinitionId;
            pe.Difficulty = (Persistence.Difficulty)(int)f.Difficulty;
            pe.PainOrDiscomfort = f.PainOrDiscomfort;
            pe.BreathingDifficulty = f.BreathingDifficulty;
            pe.FormIssues = f.FormIssues;
            pe.Comment = f.Comment;
        }
    }

    private static WorkoutSession ToDomain(Persistence.WorkoutSession row)
    {
        var sets = row.Sets.Select(s => WorkoutSet.Rehydrate(s.Id, s.ExerciseDefinitionId, s.SetNumber,
            new SetResult(s.Reps, s.Weight, s.DurationSeconds, s.RestSeconds), s.Completed));
        var feedback = row.Feedback.Select(f => WorkoutFeedback.Rehydrate(f.Id, f.ExerciseDefinitionId,
            (Difficulty)(int)f.Difficulty, f.PainOrDiscomfort, f.BreathingDifficulty, f.FormIssues, f.Comment));
        return WorkoutSession.Rehydrate(row.Id, row.Date, row.RoutineId, row.StartedAt, row.EndedAt,
            row.TotalSeconds, row.Notes, sets, feedback);
    }
}

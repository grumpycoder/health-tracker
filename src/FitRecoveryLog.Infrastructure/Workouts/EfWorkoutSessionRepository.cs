using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Infrastructure.Workouts;

/// <summary>
/// EF implementation of the session queries the routine use cases need: count a routine's
/// sessions (so a routine with history is archived, not deleted) and, for the explicit cascade
/// path, soft-delete them. Load + save (not ExecuteUpdate/Delete) so <see cref="AppDbContext"/>
/// stamps the tombstone and the change syncs.
/// </summary>
public sealed class EfWorkoutSessionRepository : IWorkoutSessionRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfWorkoutSessionRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<int> CountByRoutineAsync(Guid routineId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.WorkoutSessions.CountAsync(s => s.RoutineId == routineId, ct);
    }

    public async Task DeleteByRoutineAsync(Guid routineId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var sessions = await db.WorkoutSessions.Where(s => s.RoutineId == routineId).ToListAsync(ct);
        if (sessions.Count == 0) return;
        foreach (var s in sessions) { s.IsDeleted = true; s.DeletedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
}

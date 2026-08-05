using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Infrastructure.Workouts;

/// <summary>
/// EF implementation of the session operation the routine use cases need. Detaches (rather
/// than deletes) a routine's past sessions so their history survives when the routine is
/// deleted. Load + save (not ExecuteUpdate) so <see cref="AppDbContext"/> stamps UpdatedAt
/// and the change syncs.
/// </summary>
public sealed class EfWorkoutSessionRepository : IWorkoutSessionRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfWorkoutSessionRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task DetachFromRoutineAsync(Guid routineId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var sessions = await db.WorkoutSessions.Where(s => s.RoutineId == routineId).ToListAsync(ct);
        if (sessions.Count == 0) return;
        foreach (var s in sessions) s.RoutineId = null;
        await db.SaveChangesAsync(ct);
    }
}

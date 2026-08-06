using FitRecoveryLog.Application.Workouts;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Web implementation of the session queries the routine use cases need: count a
/// routine's sessions (to protect history from deletion) and, for the explicit cascade path,
/// soft-delete them.</summary>
public sealed class ApiWorkoutSessionRepository : IWorkoutSessionRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiWorkoutSessionRepository(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    public async Task<int> CountByRoutineAsync(Guid routineId, CancellationToken ct = default) =>
        WebSyncClient.Rows<Persistence.WorkoutSession>(await _state.DataAsync())
            .Count(s => s.RoutineId == routineId);

    public async Task DeleteByRoutineAsync(Guid routineId, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var sessions = WebSyncClient.Rows<Persistence.WorkoutSession>(pull)
            .Where(s => s.RoutineId == routineId).ToList();
        if (sessions.Count == 0) return;
        foreach (var s in sessions) { s.IsDeleted = true; s.DeletedAt = DateTime.UtcNow; }
        await _sync.PushAsync(sessions);
        _state.Invalidate();
    }
}

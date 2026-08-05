using FitRecoveryLog.Application.Workouts;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Web implementation: detaches a deleted routine's sessions by pushing them with a
/// null RoutineId, preserving their history.</summary>
public sealed class ApiWorkoutSessionRepository : IWorkoutSessionRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiWorkoutSessionRepository(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    public async Task DetachFromRoutineAsync(Guid routineId, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var sessions = WebSyncClient.Rows<Persistence.WorkoutSession>(pull)
            .Where(s => s.RoutineId == routineId).ToList();
        if (sessions.Count == 0) return;
        foreach (var s in sessions) s.RoutineId = null;
        await _sync.PushAsync(sessions);
        _state.Invalidate();
    }
}

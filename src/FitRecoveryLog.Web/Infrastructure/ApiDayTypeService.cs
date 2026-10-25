using FitRecoveryLog.Application.Workouts;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Web implementation of the day-type port: marks a date as a workout day by
/// upserting its daily log via the sync API (invoked by the WorkoutCompleted handler), and
/// resets it to Recovery when the day's last workout is deleted.</summary>
public sealed class ApiDayTypeService : IDayTypeService
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiDayTypeService(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    public async Task MarkWorkoutDayAsync(DateOnly date, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var log = WebSyncClient.Rows<Persistence.DailyLog>(pull).FirstOrDefault(d => d.Date == date);
        // Active Recovery / High Workload are deliberate and left alone; Unset counts as Recovery.
        if (log is not null && log.DayType is not (Persistence.DayType.Unset or Persistence.DayType.Recovery)) return;
        log ??= new Persistence.DailyLog { Date = date };
        log.DayType = Persistence.DayType.Workout;
        await _sync.PushAsync(log);
        _state.Invalidate();
    }

    public async Task ResetWorkoutDayAsync(DateOnly date, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var log = WebSyncClient.Rows<Persistence.DailyLog>(pull).FirstOrDefault(d => d.Date == date);
        if (log?.DayType != Persistence.DayType.Workout) return;
        log.DayType = Persistence.DayType.Recovery;
        await _sync.PushAsync(log);
        _state.Invalidate();
    }
}

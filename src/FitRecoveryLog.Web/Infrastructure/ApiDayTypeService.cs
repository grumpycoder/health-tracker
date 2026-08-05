using FitRecoveryLog.Application.Workouts;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Web implementation of the day-type port: marks a date as a workout day by
/// upserting its daily log via the sync API. Invoked by the WorkoutCompleted handler.</summary>
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
        var log = WebSyncClient.Rows<Persistence.DailyLog>(pull).FirstOrDefault(d => d.Date == date)
                  ?? new Persistence.DailyLog { Date = date };
        log.DayType = Persistence.DayType.Workout;
        await _sync.PushAsync(log);
        _state.Invalidate();
    }
}

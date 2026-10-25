using FitRecoveryLog.Domain.Workouts;

namespace FitRecoveryLog.Application.Workouts;

/// <summary>Persistence port for the <see cref="WorkoutSession"/> aggregate. Implemented per
/// client (phone over EF, web over the sync API).</summary>
public interface IWorkoutRepository
{
    Task<WorkoutSession?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkoutSession>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(WorkoutSession session, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Cross-aggregate effect on the daily log's day type. A day is Recovery unless a workout
/// says otherwise (an Unset day counts as Recovery): completing a workout makes an Unset/Recovery
/// day a Workout day, and deleting the day's last workout makes a Workout day Recovery again.
/// Active Recovery and High Workload are deliberate choices and are never changed. Kept as a
/// focused port so the enum/DailyLog stay out of the workout domain.</summary>
public interface IDayTypeService
{
    Task MarkWorkoutDayAsync(DateOnly date, CancellationToken ct = default);
    Task ResetWorkoutDayAsync(DateOnly date, CancellationToken ct = default);
}

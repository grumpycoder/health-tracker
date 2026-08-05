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

/// <summary>Cross-aggregate effect: completing a workout marks its date as a workout day on
/// the daily log. Kept as a focused port so the enum/DailyLog stay out of the workout domain.</summary>
public interface IDayTypeService
{
    Task MarkWorkoutDayAsync(DateOnly date, CancellationToken ct = default);
}

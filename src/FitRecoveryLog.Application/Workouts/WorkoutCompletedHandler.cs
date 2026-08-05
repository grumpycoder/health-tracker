using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Workouts.Events;

namespace FitRecoveryLog.Application.Workouts;

/// <summary>Reacts to a completed workout by marking its date as a workout day. This is the
/// cross-aggregate rule, now expressed as an event reaction rather than inline orchestration.</summary>
public sealed class WorkoutCompletedHandler : IDomainEventHandler<WorkoutCompleted>
{
    private readonly IDayTypeService _days;
    public WorkoutCompletedHandler(IDayTypeService days) => _days = days;

    public Task HandleAsync(WorkoutCompleted domainEvent, CancellationToken ct = default) =>
        _days.MarkWorkoutDayAsync(domainEvent.Date, ct);
}

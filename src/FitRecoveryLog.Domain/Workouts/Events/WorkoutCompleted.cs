using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Workouts.Events;

/// <summary>Raised when a workout is finished. Handlers react — e.g. marking its date as a
/// workout day on the daily log.</summary>
public sealed record WorkoutCompleted(Guid SessionId, DateOnly Date) : IDomainEvent;

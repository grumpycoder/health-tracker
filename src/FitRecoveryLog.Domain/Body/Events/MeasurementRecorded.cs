using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Body.Events;

/// <summary>Raised when a body measurement carrying a weight or waist value is recorded or
/// edited. Handlers react — e.g. mirroring the value to Apple Health on the phone.</summary>
public sealed record MeasurementRecorded(Guid MeasurementId, DateOnly Date, double? WeightLbs, double? WaistInches)
    : IDomainEvent;

using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Body.Events;

namespace FitRecoveryLog.Application.Body;

/// <summary>Mirrors a recorded weight/waist to the health store via <see cref="IHealthMirror"/>.
/// Registered only where a mirror exists (the phone); on the web the event goes unhandled.</summary>
public sealed class MeasurementRecordedHandler : IDomainEventHandler<MeasurementRecorded>
{
    private readonly IHealthMirror _mirror;
    public MeasurementRecordedHandler(IHealthMirror mirror) => _mirror = mirror;

    public async Task HandleAsync(MeasurementRecorded e, CancellationToken ct = default)
    {
        if (e.WeightLbs is { } lbs) await _mirror.WriteWeightAsync(e.Date, lbs, e.MeasurementId, ct);
        if (e.WaistInches is { } waist) await _mirror.WriteWaistAsync(e.Date, waist, e.MeasurementId, ct);
    }
}

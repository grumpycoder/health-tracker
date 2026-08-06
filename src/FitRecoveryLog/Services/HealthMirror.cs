using FitRecoveryLog.Application.Body;

namespace FitRecoveryLog.Services;

/// <summary>
/// Phone adapter for the <see cref="IHealthMirror"/> port: forwards recorded measurements to
/// Apple Health via <see cref="IHealthService"/>. No-op when Health isn't available, and it
/// swallows write failures so a HealthKit hiccup never fails the measurement save.
/// </summary>
public sealed class HealthMirror : IHealthMirror
{
    private readonly IHealthService _health;
    public HealthMirror(IHealthService health) => _health = health;

    public async Task WriteWeightAsync(DateOnly date, double lbs, Guid measurementId, CancellationToken ct = default)
    {
        if (!_health.IsAvailable) return;
        try { await _health.WriteWeightAsync(date, lbs, measurementId); } catch { }
    }

    public async Task WriteWaistAsync(DateOnly date, double inches, Guid measurementId, CancellationToken ct = default)
    {
        if (!_health.IsAvailable) return;
        try { await _health.WriteWaistAsync(date, inches, measurementId); } catch { }
    }
}

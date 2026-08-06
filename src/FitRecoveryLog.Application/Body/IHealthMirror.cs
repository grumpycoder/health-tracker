namespace FitRecoveryLog.Application.Body;

/// <summary>
/// Port for mirroring recorded measurements to an external health store (Apple Health on the
/// phone). Keeps the platform-specific HealthKit dependency out of the domain and application:
/// the phone supplies an adapter, other clients (web) simply don't register one.
/// </summary>
public interface IHealthMirror
{
    Task WriteWeightAsync(DateOnly date, double lbs, Guid measurementId, CancellationToken ct = default);
    Task WriteWaistAsync(DateOnly date, double inches, Guid measurementId, CancellationToken ct = default);
}

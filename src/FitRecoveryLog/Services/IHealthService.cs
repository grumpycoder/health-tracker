namespace FitRecoveryLog.Services;

/// <summary>
/// Apple Health (HealthKit) access. iOS-only; a no-op implementation is used on
/// other platforms (Mac Catalyst has no HealthKit).
/// </summary>
public interface IHealthService
{
    bool IsAvailable { get; }

    /// <summary>Prompt for read/write access (body mass, step count). Returns false if unavailable/denied.</summary>
    Task<bool> RequestAuthorizationAsync();

    /// <summary>Write a body-mass sample to Health, tagged with the source measurement id.</summary>
    Task WriteWeightAsync(DateOnly date, double pounds, Guid sourceId);

    /// <summary>Body-mass samples since the given time (pounds), newest first; excludes samples this app wrote.</summary>
    Task<IReadOnlyList<(DateOnly Date, double Pounds)>> ReadWeightsAsync(DateTime since);

    /// <summary>Total step count for a single day, or null if unavailable.</summary>
    Task<int?> ReadStepsAsync(DateOnly date);

    /// <summary>Asleep hours per night (keyed by the date you woke up) since the given time.</summary>
    Task<IReadOnlyList<(DateOnly Date, double Hours)>> ReadSleepAsync(DateTime since);
}

/// <summary>Used on non-iOS targets (and when HealthKit is unavailable).</summary>
public sealed class NoopHealthService : IHealthService
{
    public bool IsAvailable => false;
    public Task<bool> RequestAuthorizationAsync() => Task.FromResult(false);
    public Task WriteWeightAsync(DateOnly date, double pounds, Guid sourceId) => Task.CompletedTask;
    public Task<IReadOnlyList<(DateOnly, double)>> ReadWeightsAsync(DateTime since) =>
        Task.FromResult<IReadOnlyList<(DateOnly, double)>>(Array.Empty<(DateOnly, double)>());
    public Task<int?> ReadStepsAsync(DateOnly date) => Task.FromResult<int?>(null);
    public Task<IReadOnlyList<(DateOnly, double)>> ReadSleepAsync(DateTime since) =>
        Task.FromResult<IReadOnlyList<(DateOnly, double)>>(Array.Empty<(DateOnly, double)>());
}

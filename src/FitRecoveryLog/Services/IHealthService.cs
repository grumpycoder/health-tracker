namespace FitRecoveryLog.Services;

/// <summary>
/// Apple Health (HealthKit) access. iOS-only; a no-op implementation is used on
/// other platforms (Mac Catalyst has no HealthKit).
/// </summary>
public interface IHealthService
{
    bool IsAvailable { get; }

    /// <summary>Prompt for read/write access (body mass, waist, workouts, step count). Returns false if unavailable/denied.</summary>
    Task<bool> RequestAuthorizationAsync();

    /// <summary>Write a body-mass sample to Health, tagged with the source measurement id.</summary>
    Task WriteWeightAsync(DateOnly date, double pounds, Guid sourceId);

    /// <summary>Body-mass samples since the given time (pounds), newest first; excludes samples this app wrote.</summary>
    Task<IReadOnlyList<(DateOnly Date, double Pounds)>> ReadWeightsAsync(DateTime since);

    /// <summary>Write a waist-circumference sample to Health, tagged with the source measurement id.</summary>
    Task WriteWaistAsync(DateOnly date, double inches, Guid sourceId);

    /// <summary>Waist samples since the given time (inches), newest first; excludes samples this app wrote.</summary>
    Task<IReadOnlyList<(DateOnly Date, double Inches)>> ReadWaistsAsync(DateTime since);

    /// <summary>Write a completed strength workout to Health (shows up in Fitness).
    /// <paramref name="bodyWeightLbs"/> scales the estimated active-energy burn;
    /// null falls back to a flat per-minute rate.</summary>
    Task WriteWorkoutAsync(DateTime start, DateTime end, string name, double? bodyWeightLbs);

    /// <summary>Total step count for a single day, or null if unavailable.</summary>
    Task<int?> ReadStepsAsync(DateOnly date);

    /// <summary>Sleep per night (keyed by the date you woke up) since the given time.</summary>
    Task<IReadOnlyList<SleepNight>> ReadSleepAsync(DateTime since);
}

/// <summary>One night from Health. <see cref="HasDetail"/> is false when the source
/// recorded only a single undifferentiated block (no stages, no gaps) — too thin to
/// estimate interruptions or a score from.</summary>
public sealed record SleepNight(DateOnly Date, double Hours, int Interruptions, double DeepRemHours, bool HasDetail);

/// <summary>Used on non-iOS targets (and when HealthKit is unavailable).</summary>
public sealed class NoopHealthService : IHealthService
{
    public bool IsAvailable => false;
    public Task<bool> RequestAuthorizationAsync() => Task.FromResult(false);
    public Task WriteWeightAsync(DateOnly date, double pounds, Guid sourceId) => Task.CompletedTask;
    public Task<IReadOnlyList<(DateOnly, double)>> ReadWeightsAsync(DateTime since) =>
        Task.FromResult<IReadOnlyList<(DateOnly, double)>>(Array.Empty<(DateOnly, double)>());
    public Task WriteWaistAsync(DateOnly date, double inches, Guid sourceId) => Task.CompletedTask;
    public Task<IReadOnlyList<(DateOnly, double)>> ReadWaistsAsync(DateTime since) =>
        Task.FromResult<IReadOnlyList<(DateOnly, double)>>(Array.Empty<(DateOnly, double)>());
    public Task WriteWorkoutAsync(DateTime start, DateTime end, string name, double? bodyWeightLbs) => Task.CompletedTask;
    public Task<int?> ReadStepsAsync(DateOnly date) => Task.FromResult<int?>(null);
    public Task<IReadOnlyList<SleepNight>> ReadSleepAsync(DateTime since) =>
        Task.FromResult<IReadOnlyList<SleepNight>>(Array.Empty<SleepNight>());
}

using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Workouts;

/// <summary>
/// What was actually performed on a set — reps, weight, hold duration, and rest. A value
/// object; components are optional (a rep exercise has no duration, a plank has no reps) and
/// present ones must be non-negative.
/// </summary>
public sealed class SetResult : ValueObject
{
    public int? Reps { get; }
    public double? Weight { get; }
    public int? DurationSeconds { get; }
    public int? RestSeconds { get; }

    public static readonly SetResult None = new(null, null, null, null);

    public SetResult(int? reps, double? weight, int? durationSeconds, int? restSeconds)
    {
        if (reps < 0 || weight < 0 || durationSeconds < 0 || restSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(reps), "Set values cannot be negative.");
        Reps = reps;
        Weight = weight;
        DurationSeconds = durationSeconds;
        RestSeconds = restSeconds;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Reps;
        yield return Weight;
        yield return DurationSeconds;
        yield return RestSeconds;
    }
}

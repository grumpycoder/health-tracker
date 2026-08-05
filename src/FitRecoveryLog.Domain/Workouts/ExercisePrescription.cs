using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Workouts;

/// <summary>
/// The per-routine prescription for an exercise: the numeric targets plus a free-text nuance
/// the numbers can't hold ("10-12", "each side", "AMRAP"). A value object — all components
/// optional (a routine may prescribe reps but not weight), present ones must be non-negative.
/// </summary>
public sealed class ExercisePrescription : ValueObject
{
    public int? TargetSets { get; }
    public int? TargetReps { get; }
    public int? TargetDurationSeconds { get; }
    public int? RestSeconds { get; }
    public double? TargetWeight { get; }
    public string? TargetNote { get; }

    public static readonly ExercisePrescription None = new(null, null, null, null, null, null);

    public ExercisePrescription(int? targetSets, int? targetReps, int? targetDurationSeconds,
                                int? restSeconds, double? targetWeight, string? targetNote)
    {
        if (targetSets < 0 || targetReps < 0 || targetDurationSeconds < 0 ||
            restSeconds < 0 || targetWeight < 0)
            throw new ArgumentOutOfRangeException(nameof(targetSets), "Prescription values cannot be negative.");

        TargetSets = targetSets;
        TargetReps = targetReps;
        TargetDurationSeconds = targetDurationSeconds;
        RestSeconds = restSeconds;
        TargetWeight = targetWeight;
        TargetNote = string.IsNullOrWhiteSpace(targetNote) ? null : targetNote.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return TargetSets;
        yield return TargetReps;
        yield return TargetDurationSeconds;
        yield return RestSeconds;
        yield return TargetWeight;
        yield return TargetNote;
    }
}

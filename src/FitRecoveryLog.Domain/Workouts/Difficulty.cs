namespace FitRecoveryLog.Domain.Workouts;

/// <summary>Subjective difficulty of an exercise after a workout. Domain-owned; the
/// persistence layer maps to/from its storage enum (same ordinal values).</summary>
public enum Difficulty
{
    Unset = 0,
    Easy,
    Moderate,
    Hard,
    VeryHard,
}

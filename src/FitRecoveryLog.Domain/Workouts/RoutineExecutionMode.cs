namespace FitRecoveryLog.Domain.Workouts;

/// <summary>How a routine's sets are ordered when performed/logged. Domain-owned; the
/// persistence layer maps to/from its storage enum (same ordinal values).</summary>
public enum RoutineExecutionMode
{
    /// <summary>All sets of one exercise, then the next exercise (A1,A2,A3,B1,B2,B3…).</summary>
    StraightSets = 0,
    /// <summary>One set of each exercise per round, rotating (A1,B1,C1,A2,B2,C2…).</summary>
    Circuit = 1,
}

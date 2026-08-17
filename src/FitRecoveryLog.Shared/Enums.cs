namespace FitRecoveryLog.Data;

/// <summary>Planned status for a given day, shown on the dashboard.</summary>
public enum DayType
{
    Unset = 0,
    Workout,
    Recovery,
    ActiveRecovery,
    HighWorkload
}

/// <summary>How an exercise is measured: by repetitions or by time held/performed.</summary>
public enum ExerciseMeasure
{
    Reps = 0,
    Duration
}

/// <summary>Order sets are performed in when running a routine.</summary>
public enum RoutineExecutionMode
{
    /// <summary>All sets of one exercise, then the next exercise (A1,A2,A3,B1,B2,B3…).</summary>
    StraightSets = 0,
    /// <summary>One set of each exercise per round, rotating (A1,B1,C1,A2,B2,C2…).</summary>
    Circuit = 1
}

public enum ReminderRepeat
{
    Once = 0,
    Daily,
    Weekly,
    Monthly,
    // Appended (not inserted) so existing stored values stay valid.
    Biweekly
}

public enum MealType
{
    Breakfast = 0,
    Lunch,
    Dinner,
    Snack,
    Drink
}

/// <summary>Subjective difficulty rating for an exercise after a workout.</summary>
public enum Difficulty
{
    Unset = 0,
    Easy,
    Moderate,
    Hard,
    VeryHard
}

public enum Satiety
{
    Unset = 0,
    StillHungry,
    Satisfied,
    Full,
    Bloated,
    EmptyStomach
}

public enum Intensity
{
    Light = 0,
    Moderate,
    Heavy
}

public enum SorenessSeverity
{
    None = 0,
    Mild,
    Moderate,
    Severe
}

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

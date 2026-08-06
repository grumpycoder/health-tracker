namespace FitRecoveryLog.Data;

/// <summary>
/// Cadence config for a built-in derived reminder (body measurement, lab check,
/// weekly review). Reminders aren't hand-created — they're generated from these
/// settings and from medication schedules.
/// </summary>
public class ReminderSetting : EntityBase
{
    /// <summary>Stable key: "measurement", "labCheck", "weeklyReview".</summary>
    public string Key { get; set; } = "";
    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.Weekly;
    public TimeOnly Time { get; set; } = new(9, 0);
    public bool Active { get; set; }
    /// <summary>Stable id used to schedule/cancel the OS notification.</summary>
    public int NotificationId { get; set; }
    /// <summary>For weekly reminders: which day it fires (0=Sunday..6=Saturday).
    /// Null is treated as Sunday, so a weekly reminder fires once on a fixed day
    /// instead of floating to whatever weekday the app was last opened.</summary>
    public int? DayOfWeek { get; set; }
}

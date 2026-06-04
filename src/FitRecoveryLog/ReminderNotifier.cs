using FitRecoveryLog.Data;
using Plugin.LocalNotification;

namespace FitRecoveryLog;

/// <summary>Schedules/cancels OS local notifications for reminders and med schedules.</summary>
public static class ReminderNotifier
{
    /// <summary>True only on real iOS — the plugin has no Mac Catalyst implementation,
    /// and OperatingSystem.IsIOS() alone is also true under Catalyst.</summary>
    public static bool IsSupported => OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst();

    public static Task<bool> RequestPermissionAsync() =>
        IsSupported
            ? LocalNotificationCenter.Current.RequestNotificationPermission()
            : Task.FromResult(true);

    public static void Cancel(int notificationId)
    {
        if (!IsSupported) return;
        LocalNotificationCenter.Current.Cancel(notificationId);
    }

    /// <summary>Schedule (or cancel, if inactive/past) a recurring local notification.</summary>
    public static async Task ScheduleAsync(int notificationId, string title, string? notes,
        DateTime nextDue, ReminderRepeat repeat, bool active)
    {
        if (!IsSupported) return;
        LocalNotificationCenter.Current.Cancel(notificationId);
        if (!active) return;
        if (repeat == ReminderRepeat.Once && nextDue <= DateTime.Now) return; // would never fire

        var (repeatType, interval) = Map(repeat);
        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = notificationId,
            Title = title,
            Description = notes ?? string.Empty,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = nextDue,
                RepeatType = repeatType,
                NotifyRepeatInterval = interval
            }
        });
    }

    /// <summary>One-shot "hold complete" alert for the in-workout set timer, so the
    /// end of a timed hold still reaches the user if the device locks or the app
    /// is backgrounded (the in-app beep/haptic only fire in the foreground).</summary>
    public const int HoldTimerNotificationId = 3001;

    public static async Task ScheduleHoldEndAsync(string exerciseName, int seconds)
    {
        if (!IsSupported) return;
        LocalNotificationCenter.Current.Cancel(HoldTimerNotificationId);
        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = HoldTimerNotificationId,
            Title = "Hold complete ✅",
            Description = $"{exerciseName} — {seconds}s done",
            Schedule = new NotificationRequestSchedule { NotifyTime = DateTime.Now.AddSeconds(seconds) }
        });
    }

    public static void CancelHoldEnd() => Cancel(HoldTimerNotificationId);

    public static Task ScheduleAsync(MedicationSchedule s)
    {
        var active = s.Active && (s.EndDate is null || s.EndDate.Value >= DateOnly.FromDateTime(DateTime.Now));
        return ScheduleAsync(s.NotificationId, $"Take {s.Name}", s.Dose, NextDue(s), s.Repeat, active);
    }

    public static Task ScheduleAsync(ReminderSetting s, string title) =>
        ScheduleAsync(s.NotificationId, title, null, NextDue(s), s.Repeat, s.Active);

    public static DateTime NextDue(MedicationSchedule s) =>
        NextOccurrence(s.StartDate, s.ReminderTime, s.Repeat);

    public static DateTime NextDue(ReminderSetting s) =>
        NextOccurrence(DateOnly.FromDateTime(DateTime.Now), s.Time, s.Repeat);

    /// <summary>Next future occurrence at the given time, stepping by the repeat.</summary>
    public static DateTime NextOccurrence(DateOnly start, TimeOnly time, ReminderRepeat repeat)
    {
        var when = start.ToDateTime(time);
        var now = DateTime.Now;
        while (when < now && repeat != ReminderRepeat.Once)
        {
            when = repeat switch
            {
                ReminderRepeat.Daily => when.AddDays(1),
                ReminderRepeat.Weekly => when.AddDays(7),
                ReminderRepeat.Biweekly => when.AddDays(14),
                ReminderRepeat.Monthly => when.AddMonths(1),
                _ => when
            };
        }
        return when;
    }

    private static (NotificationRepeat Repeat, TimeSpan? Interval) Map(ReminderRepeat r) => r switch
    {
        ReminderRepeat.Daily => (NotificationRepeat.Daily, null),
        ReminderRepeat.Weekly => (NotificationRepeat.Weekly, null),
        ReminderRepeat.Biweekly => (NotificationRepeat.TimeInterval, TimeSpan.FromDays(14)),
        ReminderRepeat.Monthly => (NotificationRepeat.TimeInterval, TimeSpan.FromDays(30)),
        _ => (NotificationRepeat.No, null),
    };
}

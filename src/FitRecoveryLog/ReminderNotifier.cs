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

    /// <summary>Cancels pending OS notifications that no longer belong to any known
    /// schedule/setting — e.g. a med removed by a reseed/restore while its recurring
    /// notification lived on. The transient hold-timer alert is always kept.</summary>
    public static async Task CancelOrphanedAsync(IReadOnlySet<int> validIds)
    {
        if (!IsSupported) return;
        var pending = await LocalNotificationCenter.Current.GetPendingNotificationList();
        foreach (var n in pending)
            if (n.NotificationId is not (HoldTimerNotificationId or CravingTimerNotificationId or MealRatingNotificationId)
                && !validIds.Contains(n.NotificationId))
                LocalNotificationCenter.Current.Cancel(n.NotificationId);
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
    /// <summary>One-shot end-of-countdown alert for the craving ride-out timer.</summary>
    public const int CravingTimerNotificationId = 3002;

    /// <summary>Schedule a one-shot notification N seconds from now (replacing any
    /// pending one with the same id).</summary>
    public static async Task ScheduleOneShotAsync(int notificationId, string title, string description, int seconds)
    {
        if (!IsSupported) return;
        LocalNotificationCenter.Current.Cancel(notificationId);
        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = notificationId,
            Title = title,
            Description = description,
            Schedule = new NotificationRequestSchedule { NotifyTime = DateTime.Now.AddSeconds(seconds) }
        });
    }

    public static Task ScheduleHoldEndAsync(string exerciseName, int seconds) =>
        ScheduleOneShotAsync(HoldTimerNotificationId, "Hold complete ✅", $"{exerciseName} — {seconds}s done", seconds);

    public static void CancelHoldEnd() => Cancel(HoldTimerNotificationId);

    /// <summary>One-shot "rate how it sat" nudge after a meal/snack logged before eating.</summary>
    public const int MealRatingNotificationId = 3003;

    public static async Task ScheduleMealRatingAsync(string foodName, bool isSnack)
    {
        if (!IsSupported) return;
        LocalNotificationCenter.Current.Cancel(MealRatingNotificationId);
        var minutes = isSnack ? 5 : 30;
        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = MealRatingNotificationId,
            Title = "How did it sit? 🍽️",
            Description = $"Rate how \"{foodName}\" left you.",
            ReturningData = "meals?tab=log",
            Schedule = new NotificationRequestSchedule { NotifyTime = DateTime.Now.AddMinutes(minutes) }
        });
    }

    public static Task ScheduleAsync(MedicationSchedule s)
    {
        var active = s.Active && (s.EndDate is null || s.EndDate.Value >= DateOnly.FromDateTime(DateTime.Now));
        return ScheduleAsync(s.NotificationId, $"Take {s.Name}", s.Dose, NextDue(s), s.Repeat, active);
    }

    public static Task ScheduleAsync(ReminderSetting s, string title) =>
        ScheduleAsync(s.NotificationId, title, null, NextDue(s), s.Repeat, s.Active);

    public static DateTime NextDue(MedicationSchedule s) =>
        NextOccurrence(s.StartDate, s.ReminderTime, s.Repeat);

    /// <summary>The schedule's most recent due date on or before today, or null if it
    /// hasn't started yet. A dose is outstanding while nothing has been logged since
    /// this date — so missed doses stay visible as overdue until logged.</summary>
    public static DateOnly? LastDue(MedicationSchedule s)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!s.Active || s.StartDate > today) return null;
        var days = today.DayNumber - s.StartDate.DayNumber;
        return s.Repeat switch
        {
            ReminderRepeat.Once => s.StartDate,
            ReminderRepeat.Daily => today,
            ReminderRepeat.Weekly => today.AddDays(-(days % 7)),
            ReminderRepeat.Biweekly => today.AddDays(-(days % 14)),
            ReminderRepeat.Monthly => MonthlyLast(s.StartDate, today),
            _ => today
        };

        static DateOnly MonthlyLast(DateOnly start, DateOnly today)
        {
            var d = start; // handles month-end clamping via AddMonths
            while (d.AddMonths(1) <= today) d = d.AddMonths(1);
            return d;
        }
    }

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

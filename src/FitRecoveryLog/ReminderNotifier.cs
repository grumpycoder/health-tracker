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

    public static Task ScheduleAsync(Reminder r) =>
        ScheduleAsync(r.NotificationId, r.Title, r.Notes, r.NextDue, r.Repeat, r.Active);

    public static Task ScheduleAsync(MedicationSchedule s)
    {
        var active = s.Active && (s.EndDate is null || s.EndDate.Value >= DateOnly.FromDateTime(DateTime.Now));
        return ScheduleAsync(s.NotificationId, $"Take {s.Name}", s.Dose, NextDue(s), s.Repeat, active);
    }

    /// <summary>Next future occurrence at the schedule's reminder time.</summary>
    public static DateTime NextDue(MedicationSchedule s)
    {
        var when = s.StartDate.ToDateTime(s.ReminderTime);
        var now = DateTime.Now;
        while (when < now)
        {
            when = s.Repeat switch
            {
                ReminderRepeat.Daily => when.AddDays(1),
                ReminderRepeat.Weekly => when.AddDays(7),
                ReminderRepeat.Monthly => when.AddMonths(1),
                _ => when.AddYears(100) // Once: leave in the past -> won't reschedule
            };
            if (s.Repeat == ReminderRepeat.Once) break;
        }
        return when;
    }

    private static (NotificationRepeat Repeat, TimeSpan? Interval) Map(ReminderRepeat r) => r switch
    {
        ReminderRepeat.Daily => (NotificationRepeat.Daily, null),
        ReminderRepeat.Weekly => (NotificationRepeat.Weekly, null),
        ReminderRepeat.Monthly => (NotificationRepeat.TimeInterval, TimeSpan.FromDays(30)),
        _ => (NotificationRepeat.No, null),
    };
}

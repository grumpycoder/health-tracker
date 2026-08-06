using System.ComponentModel.DataAnnotations.Schema;

namespace FitRecoveryLog.Data;

/// <summary>How a medication is taken. Injection drives the site prompt.</summary>
public enum MedicationForm { Oral = 0, Injection = 1, Topical = 2 }

/// <summary>
/// A recurring medication the user can log with one tap (e.g. weekly TRT,
/// daily vitamin). Logging copies these defaults into a <see cref="MedicationEntry"/>.
/// </summary>
public class MedicationSchedule : EntityBase
{
    public string Name { get; set; } = "";
    public string? Dose { get; set; }
    /// <summary>How it's taken; Injection prompts for a site (with rotation suggestion).</summary>
    public MedicationForm Form { get; set; } = MedicationForm.Oral;
    /// <summary>Back-compat shim over <see cref="Form"/> — kept so pre-Form JSON backups
    /// restore the injection flag. Not a column.</summary>
    [NotMapped]
    public bool IsInjection
    {
        get => Form == MedicationForm.Injection;
        set
        {
            if (value) Form = MedicationForm.Injection;
            else if (Form == MedicationForm.Injection) Form = MedicationForm.Oral;
        }
    }
    public bool Active { get; set; } = true;

    // Schedule → reminders. While active and within [StartDate, EndDate], this drives
    // a recurring reminder; completing that reminder logs a dose.
    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.Daily;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public TimeOnly ReminderTime { get; set; } = new(9, 0);
    /// <summary>Stable id used to schedule/cancel this schedule's OS notification.</summary>
    public int NotificationId { get; set; }
}

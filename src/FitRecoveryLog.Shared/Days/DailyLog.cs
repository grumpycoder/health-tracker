namespace FitRecoveryLog.Data;

/// <summary>One row per calendar day: the planned day type and a freeform note.
/// (Note is legacy — freeform notes now live in <see cref="NoteEntry"/>.)</summary>
public class DailyLog : EntityBase
{
    public DateOnly Date { get; set; }
    public DayType DayType { get; set; } = DayType.Unset;
    public string? Note { get; set; }
    /// <summary>Water/fluids logged for the day, in fluid ounces (quick-add taps).</summary>
    public int WaterOz { get; set; }
    /// <summary>Apple Health resting (basal) energy for the day, kcal. Written by the phone once
    /// the day is over; null = not available (no Health data, or day not yet recorded).</summary>
    public int? RestingEnergyKcal { get; set; }
    /// <summary>Apple Health active energy for the day, kcal (already includes workouts — the
    /// Watch's, or this app's own estimate written with each session). For today it's the
    /// activity so far, refreshed whenever the app opens.</summary>
    public int? ActiveEnergyKcal { get; set; }
}

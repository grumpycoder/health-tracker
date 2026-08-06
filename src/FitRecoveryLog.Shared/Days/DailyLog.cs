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
}

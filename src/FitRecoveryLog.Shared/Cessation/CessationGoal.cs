namespace FitRecoveryLog.Data;

/// <summary>
/// A habit being quit (tobacco, alcohol, …). Streaks are derived, never stored:
/// the quit date never resets; "days since last slip" comes from events — a slip
/// does not erase progress (non-judgmental by design).
/// </summary>
public class CessationGoal : EntityBase
{
    public string Substance { get; set; } = "";
    public DateOnly QuitDate { get; set; }
    /// <summary>False = cold turkey (QuitDate is the day you stopped); true = tapering
    /// (QuitDate is the TARGET — daily allowance falls linearly from the baseline to 0
    /// between <see cref="TaperStartDate"/> and QuitDate).</summary>
    public bool Taper { get; set; }
    /// <summary>When the taper began (set on goal creation for taper goals).</summary>
    public DateOnly? TaperStartDate { get; set; }
    /// <summary>Pre-quit usage in counting units (e.g. 15 cigarettes/day).</summary>
    public double? BaselineUnitsPerDay { get; set; }
    /// <summary>Cost per purchase unit: per pack when <see cref="UnitsPerPack"/> is set,
    /// otherwise per counting unit.</summary>
    public double? CostPerUnit { get; set; }
    /// <summary>Counting unit — what gets logged and tapered ("cigarette", "drink").</summary>
    public string? UnitName { get; set; }
    /// <summary>Optional purchase unit ("pack", "carton") when cost isn't per counting unit.</summary>
    public string? PackName { get; set; }
    /// <summary>Counting units per purchase unit (e.g. 20 cigarettes per pack).</summary>
    public double? UnitsPerPack { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Stable id for milestone notifications.</summary>
    public int NotificationId { get; set; }
    /// <summary>The one trigger the user is actively trying to break, if any.</summary>
    public string? FocusTrigger { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;

namespace FitRecoveryLog.Data;

public class RecoveryEntry : EntityBase
{
    public DateOnly Date { get; set; }
    /// <summary>1-10 subjective recovery rating.</summary>
    public int? RecoveryRating { get; set; }
    /// <summary>1-10 subjective fatigue rating.</summary>
    public int? FatigueRating { get; set; }

    /// <summary>CSV of soreness locations. Use <see cref="SorenessLocationList"/>.</summary>
    public string? SorenessLocations { get; set; }
    public SorenessSeverity SorenessSeverity { get; set; } = SorenessSeverity.None;
    public string? Notes { get; set; }

    [NotMapped]
    public IReadOnlyList<string> SorenessLocationList => CsvField.Split(SorenessLocations);
}

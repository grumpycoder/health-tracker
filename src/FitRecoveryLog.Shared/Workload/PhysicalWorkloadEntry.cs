using System.ComponentModel.DataAnnotations.Schema;

namespace FitRecoveryLog.Data;

public class PhysicalWorkloadEntry : EntityBase
{
    public DateOnly Date { get; set; }
    /// <summary>Yard work, grass cutting, dog care, travel, etc.</summary>
    public string Activity { get; set; } = "";
    public int? DurationMinutes { get; set; }
    public Intensity Intensity { get; set; } = Intensity.Moderate;
    public string? BodyAreasAffected { get; set; }
    public string? Notes { get; set; }

    [NotMapped]
    public IReadOnlyList<string> BodyAreaList => CsvField.Split(BodyAreasAffected);
}

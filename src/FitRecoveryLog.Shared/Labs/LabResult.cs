namespace FitRecoveryLog.Data;

public class LabResult : EntityBase
{
    public DateOnly Date { get; set; }
    /// <summary>Testosterone, Hematocrit, PSA, A1C, etc. — freeform so custom labs work.</summary>
    public string LabName { get; set; } = "";
    public double? Value { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
}

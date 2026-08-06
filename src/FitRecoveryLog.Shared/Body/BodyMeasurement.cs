namespace FitRecoveryLog.Data;

public class BodyMeasurement : EntityBase
{
    public DateOnly Date { get; set; }
    public double? WeightLbs { get; set; }
    public double? WaistInches { get; set; }
    public double? ChestInches { get; set; }
    public double? HipsInches { get; set; }
    public double? ArmsInches { get; set; }
    public double? ThighsInches { get; set; }

    // Body-composition metrics from a smart scale (e.g. Hume). These don't sync to
    // Apple Health — Hume keeps them in-app — so they're entered manually.
    public double? BodyFatPercent { get; set; }
    public double? MuscleMassLbs { get; set; }
    public double? VisceralFat { get; set; }
    public double? BodyWaterPercent { get; set; }
    public int? BasalMetabolicRate { get; set; }
    public int? MetabolicAge { get; set; }

    public string? ClothingFitNotes { get; set; }
    /// <summary>Optional on-device path to a progress photo.</summary>
    public string? PhotoPath { get; set; }
}

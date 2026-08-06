namespace FitRecoveryLog.Data;

public class MedicationEntry : EntityBase
{
    public string Name { get; set; } = "";
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.Now;
    /// <summary>For injections (e.g. TRT): the site used.</summary>
    public string? InjectionSite { get; set; }
    public string? ReactionNotes { get; set; }
}

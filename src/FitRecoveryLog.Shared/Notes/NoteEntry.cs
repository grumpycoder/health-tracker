namespace FitRecoveryLog.Data;

/// <summary>A timestamped freeform note, logged any time of day
/// ("not hungry at normal lunch time", "felt great after workout").</summary>
public class NoteEntry : EntityBase
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Text { get; set; } = "";
}

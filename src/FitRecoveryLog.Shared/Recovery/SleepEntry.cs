namespace FitRecoveryLog.Data;

public class SleepEntry : EntityBase
{
    public DateOnly Date { get; set; }
    public double? DurationHours { get; set; }
    public int? SleepScore { get; set; }
    public int? Interruptions { get; set; }
    public string? Notes { get; set; }
    /// <summary>True when SleepScore is our heuristic estimate rather than a real
    /// score (e.g. Apple's Sleep Score). Apple's score isn't exposed to HealthKit,
    /// so imports fill an estimate; entering the real score clears this.</summary>
    public bool ScoreEstimated { get; set; }
}

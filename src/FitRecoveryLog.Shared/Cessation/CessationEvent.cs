namespace FitRecoveryLog.Data;

public enum CessationEventType { Craving = 0, Slip = 1 }

/// <summary>A craving ridden out or a slip — both are data, not failure.</summary>
public class CessationEvent : EntityBase
{
    public Guid GoalId { get; set; }
    public CessationGoal? Goal { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public CessationEventType Type { get; set; }
    /// <summary>1 (mild) – 5 (intense).</summary>
    public int? Intensity { get; set; }
    /// <summary>What set it off (stress, social, boredom, …).</summary>
    public string? Trigger { get; set; }
    /// <summary>Units consumed, for slips.</summary>
    public double? Amount { get; set; }
    public string? Notes { get; set; }
}

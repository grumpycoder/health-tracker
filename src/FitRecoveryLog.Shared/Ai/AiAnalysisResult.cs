namespace FitRecoveryLog.Data;

/// <summary>
/// The most recent AI 8-week analysis, persisted so it syncs across devices and survives a page
/// reload — run it once and every client shows the same result without paying for another LLM call.
/// Treated as a singleton (latest non-deleted row wins). The structured <see cref="AiOutcome"/> is
/// stored as JSON in <see cref="Json"/> so this entity (in Shared) stays free of the Application layer.
/// </summary>
public class AiAnalysisResult : EntityBase
{
    /// <summary>The day it was generated (informational — the analysis itself spans 8 weeks).</summary>
    public DateOnly Date { get; set; }
    /// <summary>Local "MMM d, h:mm tt" label of when it was generated ("Generated …").</summary>
    public string? When { get; set; }
    /// <summary>Serialized AiOutcome (analysis prose + structured actions/flags/trend/exercises).</summary>
    public string Json { get; set; } = "";
}

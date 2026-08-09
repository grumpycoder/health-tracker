using System.ComponentModel.DataAnnotations.Schema;

namespace FitRecoveryLog.Data;

/// <summary>
/// The AI daily check-in for one day, persisted so it syncs across devices — run it once (on the
/// phone or the web) and every client shows the same result for that date without paying for a
/// second LLM call. One row per <see cref="Date"/> (last write wins). This is a cached read model,
/// not a domain aggregate, so it's stored as a plain synced record.
/// </summary>
public class DailyCheckResult : EntityBase
{
    public DateOnly Date { get; set; }
    /// <summary>"good" | "mixed" | "poor".</summary>
    public string Tone { get; set; } = "mixed";
    public string Synopsis { get; set; } = "";
    /// <summary>Tips, newline-separated. Use <see cref="TipList"/>.</summary>
    public string? Tips { get; set; }
    /// <summary>Local "h:mm tt" label of when it was generated, shown as "checked at …".</summary>
    public string? When { get; set; }

    [NotMapped]
    public IReadOnlyList<string> TipList => string.IsNullOrWhiteSpace(Tips)
        ? Array.Empty<string>()
        : Tips.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

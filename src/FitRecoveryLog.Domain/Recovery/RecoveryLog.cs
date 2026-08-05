using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Recovery;

/// <summary>A day's subjective recovery: recovery/fatigue ratings (1-10), soreness locations
/// (reuses the <see cref="Tags"/> value object) and severity.</summary>
public sealed class RecoveryLog
{
    public Guid Id { get; }
    public DateOnly Date { get; private set; }
    public int? RecoveryRating { get; private set; }
    public int? FatigueRating { get; private set; }
    public Tags SorenessLocations { get; private set; }
    public SorenessSeverity Severity { get; private set; }
    public string? Notes { get; private set; }

    private RecoveryLog(Guid id, DateOnly date)
    {
        Id = id; Date = date; SorenessLocations = Tags.Empty;
    }

    public static RecoveryLog Create(DateOnly date) => new(Guid.NewGuid(), date);

    public static RecoveryLog Rehydrate(Guid id, DateOnly date, int? recovery, int? fatigue,
        Tags soreness, SorenessSeverity severity, string? notes)
    {
        var r = new RecoveryLog(id, date);
        r.SetRecoveryRating(recovery);
        r.SetFatigueRating(fatigue);
        r.SorenessLocations = soreness;
        r.Severity = severity;
        r.SetNotes(notes);
        return r;
    }

    public void SetDate(DateOnly date) => Date = date;
    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    public void SetSoreness(Tags locations, SorenessSeverity severity) { SorenessLocations = locations; Severity = severity; }

    public void SetRecoveryRating(int? rating) => RecoveryRating = InRange(rating);
    public void SetFatigueRating(int? rating) => FatigueRating = InRange(rating);

    // Subjective 1-10; anything else clears it.
    private static int? InRange(int? rating) => rating is >= 1 and <= 10 ? rating : null;
}

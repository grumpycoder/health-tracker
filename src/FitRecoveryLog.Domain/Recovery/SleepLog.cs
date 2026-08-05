namespace FitRecoveryLog.Domain.Recovery;

/// <summary>A night's sleep. <see cref="ScoreEstimated"/> marks a heuristic score (from import);
/// entering a real score clears it.</summary>
public sealed class SleepLog
{
    public Guid Id { get; }
    public DateOnly Date { get; private set; }
    public double? DurationHours { get; private set; }
    public int? Score { get; private set; }
    public int? Interruptions { get; private set; }
    public string? Notes { get; private set; }
    public bool ScoreEstimated { get; private set; }

    private SleepLog(Guid id, DateOnly date) { Id = id; Date = date; }

    public static SleepLog Create(DateOnly date) => new(Guid.NewGuid(), date);

    public static SleepLog Rehydrate(Guid id, DateOnly date, double? durationHours, int? score,
        int? interruptions, string? notes, bool scoreEstimated)
    {
        var s = new SleepLog(id, date);
        s.SetDuration(durationHours);
        s.SetInterruptions(interruptions);
        s.SetNotes(notes);
        s.Score = score is >= 0 and <= 100 ? score : null;
        s.ScoreEstimated = scoreEstimated;
        return s;
    }

    public void SetDate(DateOnly date) => Date = date;
    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public void SetDuration(double? hours)
    {
        if (hours < 0) throw new ArgumentOutOfRangeException(nameof(hours));
        DurationHours = hours;
    }

    public void SetInterruptions(int? count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Interruptions = count;
    }

    /// <summary>Set the sleep score. A manually-entered score is a real score (not estimated).</summary>
    public void SetScore(int? score, bool estimated)
    {
        if (score is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(score), "Score must be 0-100.");
        Score = score;
        ScoreEstimated = score is not null && estimated;
    }
}

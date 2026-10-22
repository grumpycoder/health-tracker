namespace FitRecoveryLog.Data;

/// <summary>Calories out per day plus rolling averages, built from the rows that feed
/// <see cref="EnergyMath.Out"/>. Each day uses the BMR/weight known as of that day, so older
/// days don't borrow today's body numbers. Shared by the phone, web and the CSV export.</summary>
public sealed class EnergyHistory
{
    public readonly record struct RollingAverage(int Kcal, int Days, int HealthDays);
    /// <summary>Calories in − out over a window: per-day average and total (negative = deficit).</summary>
    public readonly record struct RollingBalance(int AvgNet, int TotalNet, int Days)
    {
        /// <summary>Rough body-weight equivalent of the total (≈3,500 kcal per lb).</summary>
        public double Pounds => TotalNet / 3500.0;
    }

    private readonly Dictionary<DateOnly, DailyLog> _logs;
    private readonly List<BodyMeasurement> _body;          // oldest first
    private readonly Dictionary<DateOnly, int> _workoutSeconds;
    private readonly DateOnly _today;

    public EnergyHistory(IEnumerable<DailyLog> logs, IEnumerable<BodyMeasurement> body,
        IEnumerable<WorkoutSession> sessions, DateOnly today)
    {
        _logs = logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.UpdatedAt).First());
        _body = body.Where(b => b.WeightLbs != null || b.BasalMetabolicRate != null).OrderBy(b => b.Date).ToList();
        _workoutSeconds = sessions.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.Sum(s => s.TotalSeconds ?? 0));
        _today = today;
    }

    public DailyLog? Log(DateOnly day) => _logs.GetValueOrDefault(day);

    public EnergyMath.EnergyOut? For(DateOnly day)
    {
        BodyMeasurement? bmr = null, weight = null;
        foreach (var b in _body)
        {
            if (b.Date > day) break;
            if (b.BasalMetabolicRate != null) bmr = b;
            if (b.WeightLbs != null) weight = b;
        }
        return EnergyMath.Out(Log(day), _today, bmr?.BasalMetabolicRate, weight?.WeightLbs,
            _workoutSeconds.GetValueOrDefault(day));
    }

    /// <summary>Average calories out over the <paramref name="days"/> completed days ending on
    /// <paramref name="end"/> (or yesterday, if <paramref name="end"/> is today or later — today's
    /// burn isn't finished). Days with no estimate are skipped; null if none qualify.</summary>
    public RollingAverage? Average(DateOnly end, int days)
    {
        var outs = Window(end, days).Select(For).Where(o => o is not null).Select(o => o!.Value).ToList();
        if (outs.Count == 0) return null;
        return new((int)Math.Round(outs.Average(o => o.Kcal)), outs.Count, outs.Count(o => o.FromHealth));
    }

    /// <summary>Deficit/surplus over the same completed-day window as <see cref="Average"/>. Only days
    /// with food logged count — an unlogged day would otherwise read as a full-day deficit.</summary>
    public RollingBalance? Balance(DateOnly end, int days, IReadOnlyDictionary<DateOnly, int> caloriesIn)
    {
        var nets = Window(end, days)
            .Select(d => caloriesIn.TryGetValue(d, out var kin) && kin > 0 && For(d) is { } o ? kin - o.Kcal : (int?)null)
            .Where(n => n is not null).Select(n => n!.Value).ToList();
        if (nets.Count == 0) return null;
        return new((int)Math.Round(nets.Average()), nets.Sum(), nets.Count);
    }

    // The completed days ending on end (or yesterday, if end is today or later).
    private IEnumerable<DateOnly> Window(DateOnly end, int days)
    {
        var last = end < _today ? end : _today.AddDays(-1);
        return Enumerable.Range(0, days).Select(i => last.AddDays(-i));
    }
}

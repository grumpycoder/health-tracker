namespace FitRecoveryLog.Data;

/// <summary>
/// Compares body measurements against the goals on <see cref="GoalSettings"/>. Shared by the phone
/// (Measurements progress view, "goal reached" nudge, Weekly Review) and the web (Measurements).
/// <see cref="Overview"/> reports progress from a baseline (earliest recorded value) toward the target
/// plus a trend vs the previous measurement. Lives in Shared and works off plain <see cref="Snapshot"/>
/// values so both the Data entity and the domain aggregate can feed it.
/// </summary>
public static class GoalProgress
{
    private const double Tolerance = 0.05;

    /// <summary>The eight tracked body values at a point in time (any may be null).</summary>
    public readonly record struct Snapshot(double? Weight, double? Waist, double? BodyFat, double? Chest,
        double? Shoulders, double? Arms, double? Thighs, double? Calves);

    public enum Trend { NoData, Toward, Away, Flat, Met }

    public sealed record Metric(string Label, string Unit, double Goal, double? Current, double? Prior);

    /// <summary>One row of the full body overview: current + target, progress % from baseline, an
    /// OVERALL trend (vs baseline, drives the dot + bar) and a RECENT trend (vs the previous entry,
    /// so a wrong-way week is visible even when the overall picture is still good).</summary>
    public sealed record Row(string Label, string Unit, double? Current, double? Goal, double? Baseline,
        double? Done, double? Total, int? Percent, bool Met, Trend Trend, string Arrow, string Status,
        Trend RecentTrend, string Recent, string Achieved);

    private static readonly (string Label, string Unit, Func<GoalSettings, double?> Goal, Func<Snapshot, double?> Val)[] Defs =
    {
        ("Weight", "lb", g => g.GoalWeightLbs, s => s.Weight),
        ("Waist", "in", g => g.GoalWaistInches, s => s.Waist),
        ("Body fat", "%", g => g.GoalBodyFatPercent, s => s.BodyFat),
        ("Chest", "in", g => g.GoalChestInches, s => s.Chest),
        ("Shoulders", "in", g => g.GoalShouldersInches, s => s.Shoulders),
        ("Arms", "in", g => g.GoalArmsInches, s => s.Arms),
        ("Thighs", "in", g => g.GoalThighsInches, s => s.Thighs),
        ("Calves", "in", g => g.GoalCalvesInches, s => s.Calves),
    };

    public static Snapshot From(BodyMeasurement m) => new(m.WeightLbs, m.WaistInches, m.BodyFatPercent,
        m.ChestInches, m.ShouldersInches, m.ArmsInches, m.ThighsInches, m.CalvesInches);

    // ---- Goal-reached check for the nudge (goal-set metrics only) ----------------------------

    public static List<Metric> Build(GoalSettings? g, BodyMeasurement? current, BodyMeasurement? prior) =>
        Build(g, current is null ? (Snapshot?)null : From(current), prior is null ? (Snapshot?)null : From(prior));

    public static List<Metric> Build(GoalSettings? g, Snapshot? current, Snapshot? prior)
    {
        var list = new List<Metric>();
        if (g is null) return list;
        foreach (var d in Defs)
            if (d.Goal(g) is double target)
                list.Add(new Metric(d.Label, d.Unit, target,
                    current is { } c ? d.Val(c) : null, prior is { } p ? d.Val(p) : null));
        return list;
    }

    public static bool IsMet(Metric m) => IsMet(m.Goal, m.Current, m.Prior);

    private static bool IsMet(double goal, double? current, double? prior)
    {
        if (current is not double c) return false;
        if (Math.Abs(c - goal) <= Tolerance) return true;   // landed on target
        if (prior is not double p) return false;            // no direction to judge a crossing
        return p < goal ? c >= goal : p > goal ? c <= goal : false;
    }

    public static string Remaining(Metric m)
    {
        if (m.Current is not double c) return $"goal {m.Goal:0.#} {m.Unit}";
        var d = Math.Abs(c - m.Goal);
        return d <= Tolerance ? "reached ✓" : $"{d:0.#} {m.Unit} to go";
    }

    // ---- Full overview with progress % + trend (Measurements pages) --------------------------

    /// <summary>All eight metrics from a measurement history (oldest → newest). Per metric: baseline =
    /// earliest non-null value, current = latest, prior = the one before current. Progress is how far
    /// current has moved from baseline toward the goal; trend compares current to prior.</summary>
    public static List<Row> Overview(GoalSettings? g, IReadOnlyList<Snapshot> historyOldestFirst)
    {
        var rows = new List<Row>();
        foreach (var d in Defs)
        {
            var series = historyOldestFirst.Select(d.Val).Where(v => v is not null).Select(v => v!.Value).ToList();
            double? baseline = series.Count > 0 ? series[0] : null;   // earliest recorded value
            double? current = series.Count > 0 ? series[^1] : null;   // latest
            double? prior = series.Count > 1 ? series[^2] : null;     // the one before latest (≈ last week)
            var hasTrend = series.Count >= 2;                         // need ≥2 points to show movement
            var goal = g is null ? null : d.Goal(g);

            // Arrow = overall direction of change from the baseline (not the last single blip).
            string arrow = "";
            if (hasTrend && baseline is double b0 && current is double c0)
                arrow = c0 < b0 - Tolerance ? "↓" : c0 > b0 + Tolerance ? "↑" : "→";

            // Recent = the latest change vs the previous entry, so a wrong-way week shows up.
            var (recentTrend, recent) = Recent(goal, current, prior, d.Unit);

            if (goal is not double gl)
            {
                rows.Add(new Row(d.Label, d.Unit, current, null, baseline, null, null, null, false,
                    Trend.NoData, arrow, current is null ? "—" : "no goal set", recentTrend, recent, ""));
                continue;
            }

            // Met = at the target, or past it in the direction implied by the baseline.
            var met = current is double cm && (Math.Abs(cm - gl) <= Tolerance
                || (baseline is double bm && (gl < bm ? cm <= gl : gl > bm && cm >= gl)));

            double? done = null, total = null; int? percent = null;
            if (baseline is double b && current is double c)
            {
                var t = Math.Abs(b - gl);
                total = t;
                if (t <= Tolerance) { percent = met ? 100 : 0; done = 0; }
                else
                {
                    var toward = gl < b ? b - c : c - b;      // movement in the goal's direction
                    done = Math.Clamp(toward, 0, t);
                    percent = met ? 100 : Math.Clamp((int)Math.Round(100 * done.Value / t), 0, 100);
                }
            }

            // Deadband so a tiny wiggle reads "flat", not toward/away — scales with the journey length
            // (5% of the baseline→goal distance, min 0.1 unit). Fixes e.g. body fat 27.1→27.2 (+0.1)
            // being flagged as regressing when it's really unchanged.
            var band = Math.Max(0.1, (total ?? 0) * 0.05);

            // Overall arrow (baseline → current), suppressed to → within the deadband.
            var gArrow = arrow;
            if (baseline is double ab && current is double ac)
                gArrow = Math.Abs(ac - ab) <= band ? "→" : ac < ab ? "↓" : "↑";

            // Trend = has the gap to the goal meaningfully shrunk (toward) or grown (away) since baseline?
            Trend trend;
            if (met) trend = Trend.Met;
            else if (!hasTrend || baseline is not double bt || current is not double ct) trend = Trend.NoData;
            else
            {
                var net = Math.Abs(bt - gl) - Math.Abs(ct - gl);   // + = progressed toward goal
                trend = net > band ? Trend.Toward : net < -band ? Trend.Away : Trend.Flat;
            }

            var status = met ? "reached ✓"
                : current is double cc ? $"{Math.Abs(cc - gl):0.#} {d.Unit} to go" : $"goal {gl:0.#} {d.Unit}";
            // Accomplishment-forward label: what you've moved so far, e.g. "10.7 lb lost" / "0.3 in gained".
            var verb = baseline is double bv && gl > bv ? "gained" : "lost";
            var achieved = done is double dv && dv > Tolerance ? $"{dv:0.#} {d.Unit} {verb}" : "";
            rows.Add(new Row(d.Label, d.Unit, current, gl, baseline, done, total, percent, met, trend, gArrow, status,
                recentTrend, recent, achieved));
        }
        return rows;
    }

    // Latest change vs the previous entry: a "▲ 0.6 lb vs last" label + whether that move was toward
    // or away from the goal (so a recent reversal is visible even when the overall trend is good).
    private static (Trend, string) Recent(double? goal, double? current, double? prior, string unit)
    {
        if (current is not double c || prior is not double p) return (Trend.NoData, "");
        var delta = c - p;
        var mark = delta > Tolerance ? "▲" : delta < -Tolerance ? "▼" : "→";
        var text = $"{mark} {Math.Abs(delta):0.#} {unit} vs last";
        if (goal is not double gl) return (Trend.Flat, text);
        if (Math.Abs(delta) <= Tolerance) return (Trend.Flat, text);
        var toward = Math.Abs(c - gl) < Math.Abs(p - gl);
        return (toward ? Trend.Toward : Trend.Away, text);
    }
}

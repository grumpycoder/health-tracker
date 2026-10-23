import Foundation

/// Port of C# GoalProgress.Overview (FitRecoveryLog.Shared/Goals): per body metric, progress from the
/// earliest recorded value (baseline) toward the goal, an overall trend vs baseline, and the latest
/// change vs the previous entry.
enum GoalProgress {
    private static let tolerance = 0.05

    enum Trend { case noData, toward, away, flat, met }

    struct Row: Identifiable {
        var id: String { label }
        let label, unit: String
        let current, goal, baseline: Double?
        let percent: Int?
        let met: Bool
        let trend: Trend
        let arrow, status: String
        let recentTrend: Trend
        let recent, achieved: String
    }

    private static let defs: [(String, String, (GoalSettings) -> Double?, (BodyMeasurement) -> Double?)] = [
        ("Weight", "lb", { $0.goalWeightLbs }, { $0.weightLbs }),
        ("Waist", "in", { $0.goalWaistInches }, { $0.waistInches }),
        ("Body fat", "%", { $0.goalBodyFatPercent }, { $0.bodyFatPercent }),
        ("Chest", "in", { $0.goalChestInches }, { $0.chestInches }),
        ("Shoulders", "in", { $0.goalShouldersInches }, { $0.shouldersInches }),
        ("Arms", "in", { $0.goalArmsInches }, { $0.armsInches }),
        ("Thighs", "in", { $0.goalThighsInches }, { $0.thighsInches }),
        ("Calves", "in", { $0.goalCalvesInches }, { $0.calvesInches }),
    ]

    /// `history` oldest → newest.
    static func overview(_ g: GoalSettings?, _ history: [BodyMeasurement]) -> [Row] {
        defs.map { label, unit, goalOf, valOf in
            let series = history.compactMap(valOf)
            let baseline = series.first, current = series.last
            let prior = series.count > 1 ? series[series.count - 2] : nil
            let hasTrend = series.count >= 2
            let goal = g.flatMap(goalOf)

            var arrow = ""
            if hasTrend, let b = baseline, let c = current { arrow = c < b - tolerance ? "↓" : c > b + tolerance ? "↑" : "→" }
            let (recentTrend, recent) = recentChange(goal, current, prior, unit)

            guard let gl = goal else {
                return Row(label: label, unit: unit, current: current, goal: nil, baseline: baseline, percent: nil, met: false,
                           trend: .noData, arrow: arrow, status: current == nil ? "—" : "no goal set",
                           recentTrend: recentTrend, recent: recent, achieved: "")
            }
            var met = false
            if let c = current {
                met = abs(c - gl) <= tolerance || (baseline.map { b in gl < b ? c <= gl : gl > b && c >= gl } ?? false)
            }
            var done: Double?, total: Double?, percent: Int?
            if let b = baseline, let c = current {
                let t = abs(b - gl); total = t
                if t <= tolerance { percent = met ? 100 : 0; done = 0 }
                else {
                    let toward = gl < b ? b - c : c - b
                    done = min(max(toward, 0), t)
                    percent = met ? 100 : min(max(Int((100 * done! / t).dotnetRounded), 0), 100)
                }
            }
            let band = max(0.1, (total ?? 0) * 0.05)
            var gArrow = arrow
            if let b = baseline, let c = current { gArrow = abs(c - b) <= band ? "→" : c < b ? "↓" : "↑" }
            let trend: Trend
            if met { trend = .met }
            else if !hasTrend || baseline == nil || current == nil { trend = .noData }
            else {
                let net = abs(baseline! - gl) - abs(current! - gl)
                trend = net > band ? .toward : net < -band ? .away : .flat
            }
            let status = met ? "reached ✓" : current.map { "\(f1(abs($0 - gl))) \(unit) to go" } ?? "goal \(f1(gl)) \(unit)"
            let verb = (baseline.map { gl > $0 } ?? false) ? "gained" : "lost"
            let achieved = (done ?? 0) > tolerance ? "\(f1(done!)) \(unit) \(verb)" : ""
            return Row(label: label, unit: unit, current: current, goal: gl, baseline: baseline, percent: percent, met: met,
                       trend: trend, arrow: gArrow, status: status, recentTrend: recentTrend, recent: recent, achieved: achieved)
        }
    }

    private static func recentChange(_ goal: Double?, _ current: Double?, _ prior: Double?, _ unit: String) -> (Trend, String) {
        guard let c = current, let p = prior else { return (.noData, "") }
        let delta = c - p
        let mark = delta > tolerance ? "▲" : delta < -tolerance ? "▼" : "→"
        let text = "\(mark) \(f1(abs(delta))) \(unit) vs last"
        guard let gl = goal, abs(delta) > tolerance else { return (.flat, text) }
        return (abs(c - gl) < abs(p - gl) ? .toward : .away, text)
    }

    /// C# "0.#".
    static func f1(_ v: Double) -> String { v.formatted(.number.precision(.fractionLength(0...1))) }
}

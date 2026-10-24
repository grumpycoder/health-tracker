import Foundation

/// Morning readiness for the day ahead (0–100). Uses only what's known by the time you wake — last
/// night's sleep, overnight heart data and the training of the days BEFORE today — so the score
/// doesn't drift down after today's workout (that shows up in tomorrow's score). Each signal is
/// compared to YOUR own baseline:
/// overnight HRV (35%), sleeping heart rate (20%), last night's sleep (25%), recent training load
/// (15%). Missing signals drop out and the weights renormalize. A raised wrist temperature or
/// respiratory rate (possible illness) caps the score at 40.
///
/// Overnight signals come only from the night that ended TODAY: if it isn't in Health yet, they're
/// left out and flagged — an older night is never reused. `compute` is a pure function of its
/// inputs, which are stored with each day's result (`ReadinessSnapshot`) so a score can be audited
/// and reproduced later.
struct Readiness: Codable, Equatable {
    /// Bump when the calculation changes; snapshots are stored per version so older ones survive.
    /// v1 = calendar-day HRV + overnight-low HR (not stored); v2 = asleep-only overnight measures.
    static let algorithmVersion = 2

    struct Factor: Codable, Equatable, Identifiable {
        var id: String { name }
        let name: String
        let score: Int          // 0–100 for this signal
        let detail: String      // e.g. "12% below your norm"
        /// Nominal weight, and its share after renormalizing over the factors present.
        let weight: Double
        var share = 0.0
        /// Points of the final score this factor supplies (score × share).
        var contribution = 0.0
        var value: Double?      // current measurement, in `unit`
        var unit = ""
        var baseline: Double?   // the norm it's compared to, same unit
        var spread: Double?     // SD used for z (log units for HRV)
        var baselineN: Int?
        var z: Double?
        var ratio: Double?      // training load: last 3 days ÷ your usual 3 days
        var nights: [Day] = []  // nights the current value is taken from
        var samples: Int?
    }

    let score: Int
    /// Weighted average before rounding and any illness cap.
    var rawScore = 0.0
    let factors: [Factor]
    let flags: [String]
    let calibrating: Bool
    /// Signals left out of the score (no data, too few samples, or no baseline yet).
    var missing: [String] = []
    /// Why each one is missing or unreliable, in plain words.
    var issues: [String] = []

    var level: String { Self.level(score) }
    static func level(_ score: Int) -> String {
        switch score { case 80...: "Ready"; case 60..<80: "Moderate"; case 40..<60: "Low"; default: "Rest" }
    }
    var advice: String {
        switch score {
        case 80...: "Good day to push"
        case 60..<80: "Train as planned"
        case 40..<60: "Keep it light"
        default: "Prioritize recovery"
        }
    }
    var label: String { "\(level) — \(advice.lowercased())" }

    /// Recovery 1–10 for the synced RecoveryEntry; fatigue is its inverse.
    var recoveryRating: Int { min(10, max(1, Int((Double(score) / 10).dotnetRounded))) }
    var fatigueRating: Int { 11 - recoveryRating }

    struct Inputs: Codable, Equatable {
        var nights: [NightMetrics] = []      // one per wake day, newest first
        var load: [Day: Double] = [:]        // daily training load (difficulty-weighted minutes)
        /// Where HRV/heart rate came from (e.g. "Apple Watch"); nil = all sources.
        var vitalsSource: String?
    }

    static let minBaselineDays = 10
    /// Nights / days before `today` that `compute` reads — inputs are trimmed to this.
    static let lookback = 30

    /// Inputs trimmed to what `compute` reads and put in a fixed order, so identical Health data
    /// gives byte-identical snapshots.
    static func inputs(nights: [NightMetrics], load: [Day: Double], vitalsSource: String?, today: Day) -> Inputs {
        let first = today.adding(-lookback)
        return Inputs(nights: nights.filter { $0.day >= first && $0.day <= today }.sorted { $0.day > $1.day },
                      load: load.filter { $0.key >= first && $0.key < today }, vitalsSource: vitalsSource)
    }

    static func compute(_ x: Inputs, today: Day) -> Readiness? {
        let nights = Dictionary(x.nights.map { ($0.day, $0) }, uniquingKeysWith: { a, _ in a })
        var parts: [Factor] = []
        var calibrating = false
        var missing: [String] = [], issues: [String] = []

        func pct(_ v: Double, _ m: Double) -> String {
            let p = Int(((v - m) / m * 100).dotnetRounded)
            return p == 0 ? "at your norm" : "\(abs(p))% \(p > 0 ? "above" : "below") your norm"
        }
        func clamp(_ v: Double) -> Double { min(100, max(0, v)) }
        // Baseline-relative signals: AT your norm = 75 (a normal day is a ready day), ±20 per SD.
        func relative(_ z: Double) -> Double { clamp(75 + 20 * z) }
        func short(_ d: Day) -> String { d.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day()) }
        let src = x.vitalsSource.map { " · \($0)" } ?? ""

        let last = nights[today]
        if last == nil {
            missing += ["HRV", "sleeping HR", "sleep"]
            let latest = nights.keys.filter { $0 < today }.max()
            issues.append("Last night's sleep isn't in Apple Health yet" + (latest.map { " (latest is \(short($0)))" } ?? "")
                          + ", so sleep, HRV and heart rate are left out rather than reusing an older night. Open Health with your Watch nearby, then refresh.")
        }

        // HRV: 2–3-night average (including last night) of asleep-only HRV, in log space, vs the
        // median of the 28 nights before those.
        if let ln = last {
            let used = (0...2).compactMap { i in nights[today.adding(-i)].flatMap { n in n.hrvMs.map { (n.day, $0, n.hrvSamples) } } }
            if ln.hrvMs == nil {
                missing.append("HRV"); issues.append("No HRV readings while asleep last night, so HRV is left out.")
            } else if used.count < 2 {
                missing.append("HRV"); issues.append("Overnight HRV on only 1 of the last 3 nights (2 needed), so HRV is left out.")
            } else if let b = Overnight.hrvBaseline(nights, for: today), b.n >= 2 {
                if b.n < minBaselineDays { calibrating = true }
                let cur = used.map { log($0.1) }.reduce(0, +) / Double(used.count)
                let z = b.spread > 0 ? (cur - b.center) / b.spread : 0
                let v = exp(cur), norm = exp(b.center)
                parts.append(Factor(name: "Overnight HRV", score: Int(relative(z).dotnetRounded),
                                    detail: "\(Int(v.dotnetRounded)) ms over \(used.count) nights, \(pct(v, norm))\(src)", weight: 0.35,
                                    value: v, unit: "ms", baseline: norm, spread: b.spread, baselineN: b.n, z: z,
                                    nights: used.map(\.0), samples: used.map(\.2).reduce(0, +)))
            } else {
                missing.append("HRV"); issues.append("Fewer than 2 earlier nights with overnight HRV, so there's no HRV baseline yet.")
            }
        }
        // Sleeping HR: mean heart rate while asleep last night vs the median of the 28 nights before. Lower = better.
        if let ln = last {
            if let hr = ln.sleepingHR {
                if let b = Overnight.heartRateBaseline(nights, for: today), b.n >= 2 {
                    if b.n < minBaselineDays { calibrating = true }
                    let z = b.spread > 0 ? (hr - b.center) / b.spread : 0
                    parts.append(Factor(name: "Sleeping HR", score: Int(relative(-z).dotnetRounded),
                                        detail: "\(Int(hr.dotnetRounded)) bpm average while asleep, \(pct(hr, b.center))\(src)", weight: 0.20,
                                        value: hr, unit: "bpm", baseline: b.center, spread: b.spread, baselineN: b.n, z: z,
                                        nights: [today], samples: ln.hrSamples))
                } else {
                    missing.append("sleeping HR"); issues.append("Fewer than 2 earlier nights with sleeping heart rate, so there's no baseline yet.")
                }
            } else {
                missing.append("sleeping HR")
                issues.append("Only \(ln.hrSamples) heart-rate reading\(ln.hrSamples == 1 ? "" : "s") while asleep last night (\(Overnight.minHRSamples) needed), so sleeping HR is left out.")
            }
        }
        // Sleep: duration vs 7.5 h (70), deep+REM share vs 40% (15), efficiency vs 90% (15).
        if let ln = last, ln.sleepHours > 0 {
            let h = ln.sleepHours
            let dur = min(1, h / 7.5) * 70
            let stages = ln.deepRemHours.map { min(1, $0 / h / 0.4) * 15 } ?? 7.5
            let eff = ln.efficiency.map { min(1, $0 / 0.9) * 15 } ?? 7.5
            let m = Int((h * 60).dotnetRounded)
            parts.append(Factor(name: "Sleep", score: Int(clamp(dur + stages + eff).dotnetRounded),
                                detail: "\(m / 60)h \(String(format: "%02d", m % 60))m last night", weight: 0.25,
                                value: h, unit: "h", baseline: 7.5, nights: [today]))
        }
        // Training load: the 3 days BEFORE today vs your typical 3 days over the prior 4 weeks.
        let acute = (1...3).reduce(0.0) { $0 + (x.load[today.adding(-$1)] ?? 0) }
        let chronic = (1...28).reduce(0.0) { $0 + (x.load[today.adding(-$1)] ?? 0) } / 28 * 3
        if chronic > 0 || acute > 0 {
            let ratio = chronic > 0 ? acute / chronic : 2
            let s = clamp(100 - (ratio - 0.8) * 100)
            let word = ratio < 0.8 ? "lighter than usual" : ratio <= 1.3 ? "about your usual" : "heavier than usual"
            parts.append(Factor(name: "Training load", score: Int(s.dotnetRounded), detail: "last 3 days \(word)", weight: 0.15,
                                value: acute, unit: "min", baseline: chronic, ratio: ratio))
        }
        guard !parts.isEmpty else { return nil }

        let wsum = parts.reduce(0) { $0 + $1.weight }
        for i in parts.indices {
            parts[i].share = parts[i].weight / wsum
            parts[i].contribution = Double(parts[i].score) * parts[i].share
        }
        let raw = parts.reduce(0) { $0 + Double($1.score) * $1.weight } / wsum
        var score = Int(raw.dotnetRounded)

        // Illness warnings: last night vs the nights before the current 3-night window.
        var flags: [String] = []
        let prior = (3...lookback).compactMap { nights[today.adding(-$0)] }
        func mean(_ xs: [Double]) -> Double { xs.reduce(0, +) / Double(xs.count) }
        if let t = last?.wristTempC {
            let xs = prior.compactMap(\.wristTempC)
            if xs.count >= 5, t - mean(xs) >= 0.5 { flags.append(String(format: "Wrist temperature +%.1f °C above your norm", t - mean(xs))) }
        }
        if let r = last?.respiratory {
            let xs = prior.compactMap(\.respiratory), sd = Overnight.sd(xs)
            if xs.count >= 5, sd > 0, (r - mean(xs)) / sd >= 2 { flags.append(String(format: "Breathing rate %.1f/min, well above your norm", r)) }
        }
        if !flags.isEmpty { score = min(score, 40) }

        return Readiness(score: score, rawScore: raw, factors: parts, flags: flags, calibrating: calibrating, missing: missing, issues: issues)
    }

    /// Daily load: session minutes, weighted by how hard you rated it (Hard ×1.3, Very hard ×1.6).
    static func load(sessions: [WorkoutSession], feedback: [ExerciseFeedback]) -> [Day: Double] {
        var out: [Day: Double] = [:]
        for s in sessions {
            guard let id = s.id, let secs = s.totalSeconds, secs > 0 else { continue }
            let rated = feedback.filter { $0.workoutSessionId == id && $0.difficulty != .unset }
            let avg = rated.isEmpty ? 2.0 : Double(rated.map(\.difficulty.rawValue).reduce(0, +)) / Double(rated.count)
            let factor = avg >= 3.5 ? 1.6 : avg >= 2.5 ? 1.3 : 1.0
            out[s.date, default: 0] += Double(secs) / 60 * factor
        }
        return out
    }
}

/// One day's readiness as calculated: the exact inputs, the result and when. Stored locally per
/// (day, algorithm version); only today's is ever rewritten, so past days and older versions stay
/// as they were.
struct ReadinessSnapshot: Codable, Equatable, Identifiable {
    var id: String { "\(day.iso)/v\(algorithmVersion)" }
    let day: Day
    let algorithmVersion: Int
    var firstComputedAt: Date
    var computedAt: Date
    /// Times the result changed during the day (e.g. the Watch synced more data later).
    var revisions = 1
    var inputs: Readiness.Inputs
    var result: Readiness

    /// What to store after a calculation, or nil when inputs and result are unchanged (so repeated
    /// refreshes don't rewrite the day).
    static func next(after existing: ReadinessSnapshot?, day: Day, inputs: Readiness.Inputs, result: Readiness, now: Date) -> ReadinessSnapshot? {
        guard let e = existing, e.algorithmVersion == Readiness.algorithmVersion else {
            return ReadinessSnapshot(day: day, algorithmVersion: Readiness.algorithmVersion, firstComputedAt: now, computedAt: now, inputs: inputs, result: result)
        }
        guard e.inputs != inputs || e.result != result else { return nil }
        var s = e
        s.inputs = inputs; s.result = result; s.computedAt = now; s.revisions += 1
        return s
    }

    /// Re-run the stored inputs (current algorithm) — matches `result` when the version is current.
    func recomputed() -> Readiness? { Readiness.compute(inputs, today: day) }
}

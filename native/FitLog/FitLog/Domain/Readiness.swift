import Foundation

/// Morning readiness for the day ahead (0–100). Uses only what's known by the time you wake — last
/// night's sleep, overnight/morning heart data and the training of the days BEFORE today — so the
/// score doesn't drift down after today's workout (that shows up in tomorrow's score). Each signal
/// is compared to YOUR own recent baseline:
/// HRV (35%), resting HR (20%), last night's sleep (25%), recent training load (15%). Missing
/// signals drop out and the weights renormalize. A raised wrist temperature or respiratory rate
/// (possible illness) caps the score at 40.
struct Readiness: Equatable {
    struct Factor: Equatable, Identifiable {
        var id: String { name }
        let name: String
        let score: Int          // 0–100 for this signal
        let detail: String      // e.g. "12% below your norm"
    }

    let score: Int
    let factors: [Factor]
    let flags: [String]
    let calibrating: Bool
    /// Signals with no data in Health (e.g. HRV on a Watch/setup that doesn't record it).
    var missing: [String] = []

    var label: String {
        switch score {
        case 80...: "Ready — good day to push"
        case 60..<80: "Moderate — train as planned"
        case 40..<60: "Low — keep it light"
        default: "Rest — prioritize recovery"
        }
    }

    /// Recovery 1–10 for the synced RecoveryEntry; fatigue is its inverse.
    var recoveryRating: Int { min(10, max(1, Int((Double(score) / 10).dotnetRounded))) }
    var fatigueRating: Int { 11 - recoveryRating }

    struct Inputs {
        var hrv: [Day: Double] = [:]          // daily average HRV (ms)
        var restingHR: [Day: Double] = [:]    // overnight low HR per night (bpm), keyed by wake day
        var respiratory: [Day: Double] = [:]  // daily average breaths/min
        var wristTemp: [Day: Double] = [:]    // nightly wrist temperature (°C)
        var sleepHours: Double?
        var deepRemHours: Double?
        var efficiency: Double?
        var load: [Day: Double] = [:]         // daily training load (difficulty-weighted minutes)
        /// Last night's overnight low HR (Apple's daily resting HR keeps updating all day).
        var morningRestingHR: Double?
        /// HRV during last night's sleep, used when there's no daily series for the prior nights.
        var overnightHRV: Double?
    }

    static let minBaselineDays = 10

    static func compute(_ x: Inputs, today: Day) -> Readiness? {
        var parts: [(Factor, Double)] = []
        var calibrating = false

        // Baseline = the 30 days before the last 3 (so a bad stretch doesn't drag its own norm down).
        func baseline(_ series: [Day: Double]) -> (mean: Double, sd: Double, n: Int) {
            let xs = (4...33).compactMap { series[today.adding(-$0)] }
            guard xs.count >= 2 else { return (xs.first ?? 0, 0, xs.count) }
            let m = xs.reduce(0, +) / Double(xs.count)
            let sd = (xs.map { ($0 - m) * ($0 - m) }.reduce(0, +) / Double(xs.count - 1)).squareRoot()
            return (m, sd, xs.count)
        }
        func recent(_ series: [Day: Double], from: Int = 0, days: Int) -> Double? {
            let xs = (from..<(from + days)).compactMap { series[today.adding(-$0)] }
            return xs.isEmpty ? nil : xs.reduce(0, +) / Double(xs.count)
        }
        func pct(_ v: Double, _ m: Double) -> String {
            let p = Int(((v - m) / m * 100).dotnetRounded)
            return p == 0 ? "at your norm" : "\(abs(p))% \(p > 0 ? "above" : "below") your norm"
        }
        func clamp(_ v: Double) -> Double { min(100, max(0, v)) }
        // Baseline-relative signals: AT your norm = 75 (a normal day is a ready day), ±20 per SD.
        func relative(_ z: Double) -> Double { clamp(75 + 20 * z) }
        var missing: [String] = []

        // HRV: 3-day rolling vs baseline (single nights are noisy). Higher = better.
        let hb = baseline(x.hrv)
        if let r = recent(x.hrv, from: 1, days: 3) ?? x.overnightHRV, hb.n >= 2, hb.mean > 0 {
            if hb.n < minBaselineDays { calibrating = true }
            let z = hb.sd > 0 ? (r - hb.mean) / hb.sd : 0
            parts.append((Factor(name: "HRV", score: Int(relative(z).dotnetRounded), detail: "\(Int(r.dotnetRounded)) ms, \(pct(r, hb.mean))"), 0.35))
        } else { missing.append("HRV") }
        // Resting HR: today vs baseline. Lower = better.
        let rb = baseline(x.restingHR)
        if let r = x.morningRestingHR ?? x.restingHR[today.adding(-1)], rb.n >= 2, rb.mean > 0 {
            if rb.n < minBaselineDays { calibrating = true }
            let z = rb.sd > 0 ? (r - rb.mean) / rb.sd : 0
            parts.append((Factor(name: "Resting HR", score: Int(relative(-z).dotnetRounded), detail: "\(Int(r.dotnetRounded)) bpm overnight low, \(pct(r, rb.mean))"), 0.20))
        } else { missing.append("resting HR") }
        // Sleep: duration vs 7.5 h (70), deep+REM share vs 40% (15), efficiency vs 90% (15).
        if let h = x.sleepHours, h > 0 {
            let dur = min(1, h / 7.5) * 70
            let stages = x.deepRemHours.map { min(1, $0 / h / 0.4) * 15 } ?? 7.5
            let eff = x.efficiency.map { min(1, $0 / 0.9) * 15 } ?? 7.5
            let m = Int((h * 60).dotnetRounded)
            parts.append((Factor(name: "Sleep", score: Int(clamp(dur + stages + eff).dotnetRounded), detail: "\(m / 60)h \(String(format: "%02d", m % 60))m last night"), 0.25))
        }
        // Training load: the 3 days BEFORE today vs your typical 3 days over the prior 4 weeks.
        let acute = (1...3).reduce(0.0) { $0 + (x.load[today.adding(-$1)] ?? 0) }
        let chronic = (1...28).reduce(0.0) { $0 + (x.load[today.adding(-$1)] ?? 0) } / 28 * 3
        if chronic > 0 || acute > 0 {
            let ratio = chronic > 0 ? acute / chronic : 2
            let s = clamp(100 - (ratio - 0.8) * 100)
            let word = ratio < 0.8 ? "lighter than usual" : ratio <= 1.3 ? "about your usual" : "heavier than usual"
            parts.append((Factor(name: "Training load", score: Int(s.dotnetRounded), detail: "last 3 days \(word)"), 0.15))
        }
        guard !parts.isEmpty else { return nil }

        let wsum = parts.reduce(0) { $0 + $1.1 }
        var score = Int((parts.reduce(0) { $0 + Double($1.0.score) * $1.1 } / wsum).dotnetRounded)

        // Illness warnings vs baseline.
        var flags: [String] = []
        let tb = baseline(x.wristTemp)
        if let t = x.wristTemp[today] ?? x.wristTemp[today.adding(-1)], tb.n >= 5, t - tb.mean >= 0.5 {
            flags.append(String(format: "Wrist temperature +%.1f °C above your norm", t - tb.mean))
        }
        let bb = baseline(x.respiratory)
        if let r = x.respiratory[today] ?? x.respiratory[today.adding(-1)], bb.n >= 5, bb.sd > 0, (r - bb.mean) / bb.sd >= 2 {
            flags.append(String(format: "Breathing rate %.1f/min, well above your norm", r))
        }
        if !flags.isEmpty { score = min(score, 40) }

        return Readiness(score: score, factors: parts.map(\.0), flags: flags, calibrating: calibrating, missing: missing)
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

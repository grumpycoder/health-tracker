import Foundation

/// One HRV (ms) or heart-rate (bpm) reading with its time span — plain data, so the overnight math
/// is testable without HealthKit.
struct Reading: Codable, Equatable {
    let start: Date
    let end: Date
    let value: Double
    var mid: Date { start.addingTimeInterval(end.timeIntervalSince(start) / 2) }
}

/// What readiness and the Sleep tab both use for one night, keyed by the day the main sleep ended.
/// Sample counts are kept so a thin night can be flagged (and audited) rather than trusted.
struct NightMetrics: Codable, Equatable {
    var day: Day
    var source: String
    var bedtime: Date
    var wake: Date
    var sleepHours: Double
    var deepRemHours: Double?
    var efficiency: Double?
    /// Geometric mean of HRV (SDNN) readings taken while asleep; nil below `Overnight.minHRVSamples`.
    var hrvMs: Double?
    var hrvSamples = 0
    /// Mean heart rate while asleep; nil below `Overnight.minHRSamples`.
    var sleepingHR: Double?
    var hrSamples = 0
    var respiratory: Double?
    var wristTempC: Double?
}

/// Overnight measurements: only readings inside the night's ASLEEP segments count (not awake
/// stretches, not the daytime), and implausible values are dropped.
enum Overnight {
    static let hrvRange = 5.0...300.0
    static let hrRange = 30.0...200.0
    /// The Watch takes HRV only every few hours, so one asleep reading is a usable night (the
    /// readiness value is a 2–3-night average); heart rate is sampled every few minutes.
    static let minHRVSamples = 1
    static let minHRSamples = 10

    /// Readings whose midpoint falls inside an asleep span (spans are merged and sorted).
    static func asleep(_ rs: [Reading], in spans: [(Date, Date)]) -> [Reading] {
        rs.filter { r in spans.contains { r.mid >= $0.0 && r.mid < $0.1 } }
    }

    /// Geometric mean (HRV is right-skewed, so it's averaged in log space) and the count used.
    static func hrv(_ rs: [Reading], asleep spans: [(Date, Date)]) -> (value: Double?, n: Int) {
        let xs = asleep(rs, in: spans).map(\.value).filter { hrvRange.contains($0) }
        guard xs.count >= minHRVSamples, !xs.isEmpty else { return (nil, xs.count) }
        return (exp(xs.map(log).reduce(0, +) / Double(xs.count)), xs.count)
    }

    static func heartRate(_ rs: [Reading], asleep spans: [(Date, Date)]) -> (value: Double?, n: Int) {
        let xs = asleep(rs, in: spans).map(\.value).filter { hrRange.contains($0) }
        guard xs.count >= minHRSamples else { return (nil, xs.count) }
        return (xs.reduce(0, +) / Double(xs.count), xs.count)
    }

    static func median(_ xs: [Double]) -> Double {
        let s = xs.sorted(); let n = s.count
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2
    }

    /// Sample standard deviation (n − 1).
    static func sd(_ xs: [Double]) -> Double {
        guard xs.count >= 2 else { return 0 }
        let m = xs.reduce(0, +) / Double(xs.count)
        return (xs.map { ($0 - m) * ($0 - m) }.reduce(0, +) / Double(xs.count - 1)).squareRoot()
    }

    /// A baseline: median centre, SD spread, over `values` (already windowed by the caller).
    struct Baseline: Codable, Equatable {
        let center: Double
        let spread: Double
        let n: Int
        init?(_ values: [Double]) {
            guard !values.isEmpty else { return nil }
            center = Overnight.median(values); spread = Overnight.sd(values); n = values.count
        }
    }

    /// Nights `from…through` days before `day` (inclusive), skipping ones without the metric.
    static func window(_ nights: [Day: NightMetrics], before day: Day, from: Int, through: Int, _ f: (NightMetrics) -> Double?) -> [(Day, Double)] {
        (from...through).compactMap { i in
            let d = day.adding(-i)
            return nights[d].flatMap(f).map { (d, $0) }
        }
    }

    /// Overnight HRV baseline in log space: the 28 nights before the current 3-night window.
    static func hrvBaseline(_ nights: [Day: NightMetrics], for day: Day) -> Baseline? {
        Baseline(window(nights, before: day, from: 3, through: 30, \.hrvMs).map { log($0.1) })
    }

    /// Sleeping-HR baseline: the 28 nights before the current night.
    static func heartRateBaseline(_ nights: [Day: NightMetrics], for day: Day) -> Baseline? {
        Baseline(window(nights, before: day, from: 1, through: 28, \.sleepingHR).map(\.1))
    }
}

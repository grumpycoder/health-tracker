import Foundation

// Port of the C# NutritionMath / EnergyMath / EnergyHistory (FitRecoveryLog.Shared/Nutrition).
// Kept in step by tests/vectors/energy.json, which both the NUnit and FitLogTests suites run.

enum Nutrition {
    static func calories(_ m: [MealEntry], _ d: [DrinkEntry]) -> Int {
        m.reduce(0) { $0 + ($1.calories ?? 0) } + d.reduce(0) { $0 + ($1.calories ?? 0) }
    }
    static func protein(_ m: [MealEntry], _ d: [DrinkEntry]) -> Double { sum(m, d, \.proteinG, \.proteinG) }
    static func carbs(_ m: [MealEntry], _ d: [DrinkEntry]) -> Double { sum(m, d, \.carbsG, \.carbsG) }
    static func fat(_ m: [MealEntry], _ d: [DrinkEntry]) -> Double { sum(m, d, \.fatG, \.fatG) }
    static func fiber(_ m: [MealEntry], _ d: [DrinkEntry]) -> Double { sum(m, d, \.fiberG, \.fiberG) }
    static func fluidOz(_ d: [DrinkEntry]) -> Int { Int(d.reduce(0) { $0 + ($1.ounces ?? 0) }.rounded()) }

    private static func sum(_ m: [MealEntry], _ d: [DrinkEntry], _ mk: KeyPath<MealEntry, Double?>, _ dk: KeyPath<DrinkEntry, Double?>) -> Double {
        m.reduce(0) { $0 + ($1[keyPath: mk] ?? 0) } + d.reduce(0) { $0 + ($1[keyPath: dk] ?? 0) }
    }

    static func caloriesByDay(_ m: [MealEntry], _ d: [DrinkEntry]) -> [Day: Int] {
        var out: [Day: Int] = [:]
        for x in m { out[Day(x.time), default: 0] += x.calories ?? 0 }
        for x in d { out[Day(x.time), default: 0] += x.calories ?? 0 }
        return out.filter { $0.value > 0 }
    }
}

struct EnergyOut: Equatable { let kcal: Int; let fromHealth: Bool; var live = false }
struct RollingAverage { let kcal: Int; let days: Int; let healthDays: Int }
struct RollingBalance {
    let avgNet: Int; let totalNet: Int; let days: Int
    var pounds: Double { Double(totalNet) / 3500 }
}

struct EnergyHistory {
    private let logs: [Day: DailyLog]
    private let body: [BodyMeasurement]        // oldest first
    private let workoutSeconds: [Day: Int]
    private let today: Day

    init(logs: [DailyLog], body: [BodyMeasurement], sessions: [WorkoutSession], today: Day = .today) {
        var byDay: [Day: DailyLog] = [:]
        for l in logs where byDay[l.date].map({ ($0.updatedAt ?? .distantPast) < (l.updatedAt ?? .distantPast) }) ?? true { byDay[l.date] = l }
        self.logs = byDay
        self.body = body.filter { $0.weightLbs != nil || $0.basalMetabolicRate != nil }.sorted { $0.date < $1.date }
        self.workoutSeconds = Dictionary(sessions.map { ($0.date, $0.totalSeconds ?? 0) }, uniquingKeysWith: +)
        self.today = today
    }

    func log(_ day: Day) -> DailyLog? { logs[day] }

    func out(_ day: Day) -> EnergyOut? {
        var bmr: Int?, lbs: Double?
        for b in body {
            if b.date > day { break }
            if let v = b.basalMetabolicRate { bmr = v }
            if let v = b.weightLbs { lbs = v }
        }
        let log = logs[day]
        // Today with Health activity so far: typical resting + active so far (workouts included).
        if day >= today, let active = log?.activeEnergyKcal {
            if let rest = typicalResting(day) ?? bmr ?? lbs.flatMap({ $0 > 0 ? Int($0 * 10) : nil }) {
                return EnergyOut(kcal: rest + active, fromHealth: true, live: true)
            }
        }
        // Completed day with both Health values.
        if day < today, let rest = log?.restingEnergyKcal, rest > 0, let active = log?.activeEnergyKcal {
            return EnergyOut(kcal: rest + active, fromHealth: true)
        }
        // Estimate: BMR × 1.25 light activity + workout at MET ~5.
        guard let base = bmr ?? lbs.flatMap({ $0 > 0 ? Int($0 * 10) : nil }) else { return nil }
        let w = lbs ?? 0, secs = workoutSeconds[day] ?? 0
        let workout = (w <= 0 || secs <= 0) ? 0 : Int((Double(secs) / 60 * 5 * 3.5 * (w * 0.453592) / 200).rounded())
        return EnergyOut(kcal: Int((Double(base) * 1.25).rounded()) + workout, fromHealth: false)
    }

    private func typicalResting(_ day: Day) -> Int? {
        let rests = (1...7).compactMap { logs[day.adding(-$0)]?.restingEnergyKcal }.filter { $0 > 0 }
        return rests.isEmpty ? nil : Int((Double(rests.reduce(0, +)) / Double(rests.count)).rounded())
    }

    /// Completed days ending on `end` (or yesterday, if `end` is today or later).
    func window(_ end: Day, _ days: Int) -> [Day] {
        let last = end < today ? end : today.adding(-1)
        return (0..<days).map { last.adding(-$0) }
    }

    func average(_ end: Day, _ days: Int) -> RollingAverage? {
        let outs = window(end, days).compactMap(out)
        guard !outs.isEmpty else { return nil }
        let avg = Double(outs.reduce(0) { $0 + $1.kcal }) / Double(outs.count)
        return RollingAverage(kcal: Int(avg.rounded()), days: outs.count, healthDays: outs.filter(\.fromHealth).count)
    }

    func balance(_ end: Day, _ days: Int, caloriesIn: [Day: Int]) -> RollingBalance? {
        let nets = window(end, days).compactMap { d -> Int? in
            guard let kin = caloriesIn[d], kin > 0, let o = out(d) else { return nil }
            return kin - o.kcal
        }
        guard !nets.isEmpty else { return nil }
        let total = nets.reduce(0, +)
        return RollingBalance(avgNet: Int((Double(total) / Double(nets.count)).rounded()), totalNet: total, days: nets.count)
    }
}

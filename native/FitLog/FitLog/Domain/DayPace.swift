import Foundation

/// Where an unfinished day stands, worked out in code so the Today check-in doesn't judge partial
/// intake against full-day targets:
/// - **Pace**: the share of a day's food you normally have eaten by this time, learned from your own
///   recent logs (falling back to an even 7 AM–9 PM spread), so 1:50 PM with dinner to come isn't "low".
/// - **Calorie budget**: projected full-day expenditure (typical resting + typical activity for this
///   kind of day, or more if you've already been more active) minus your intended deficit — not the
///   fixed calorie range alone.
/// - **Protein**: grams left to reach the minimum, called unlikely only late in the eating day.
struct DayPace: Equatable {
    /// Typical share of the day's calories eaten by now (0–1), and how many past days it's based on.
    let eatenShare: Double
    let historyDays: Int
    let caloriesIn: Int
    let proteinIn: Double
    /// Projected full-day calories out, and what it's based on.
    let projectedOut: Int?
    let outBasis: String
    let deficit: ClosedRange<Int>
    let proteinTarget: ClosedRange<Int>?
    /// The fixed calorie range for today's day type (reference only — expenditure decides the budget).
    let fixedCalories: ClosedRange<Int>?

    /// End-of-day intake that lands inside the intended deficit.
    var calorieBudget: ClosedRange<Int>? {
        projectedOut.map { max(0, $0 - deficit.upperBound)...max(0, $0 - deficit.lowerBound) }
    }
    var caloriesRemaining: ClosedRange<Int>? {
        calorieBudget.map { max(0, $0.lowerBound - caloriesIn)...max(0, $0.upperBound - caloriesIn) }
    }
    var proteinRemaining: Int? { proteinTarget.map { max(0, $0.lowerBound - Int(proteinIn.rounded())) } }
    /// Mostly through the eating day: what's left of a typical day is small.
    var late: Bool { eatenShare >= 0.85 }

    /// Already past the whole day's budget — a real overshoot, whatever the time.
    var overBudget: Bool { calorieBudget.map { caloriesIn > $0.upperBound + 100 } ?? false }
    /// Late in the day and well short of the budget — a deficit bigger than intended is likely.
    var deficitTooLarge: Bool { late && (caloriesRemaining.map { $0.lowerBound > 400 } ?? false) }
    /// Late in the day with a lot of protein still to go.
    var proteinUnlikely: Bool {
        guard late, let left = proteinRemaining, let t = proteinTarget else { return false }
        return left > max(25, t.lowerBound / 4)
    }
    /// Expected intake by now if the day follows your usual pattern (for "on pace" wording).
    var expectedCaloriesByNow: Int? { calorieBudget.map { Int((Double($0.lowerBound + $0.upperBound) / 2 * eatenShare).rounded()) } }
    var expectedProteinByNow: Int? { proteinTarget.map { Int((Double($0.lowerBound) * eatenShare).rounded()) } }

    /// Default eating window when there isn't enough history: even from 7 AM to 9 PM.
    static func defaultShare(minuteOfDay m: Int) -> Double { min(1, max(0, Double(m - 7 * 60) / Double(14 * 60))) }

    static func make(_ d: Coach.Inputs, now: Date, deficit: ClosedRange<Int>) -> DayPace {
        let day = d.today
        let minute = Calendar.current.component(.hour, from: now) * 60 + Calendar.current.component(.minute, from: now)
        func minuteOf(_ t: Date) -> Int { Calendar.current.component(.hour, from: t) * 60 + Calendar.current.component(.minute, from: t) }

        // Your usual pace: for each of the last 21 completed days with a real day's logging, the share of
        // its calories logged by this clock time. Median, so one odd day doesn't swing it.
        var shares: [Double] = []
        for i in 1...21 {
            let p = day.adding(-i), m = d.meals[p] ?? [], dr = d.drinks[p] ?? []
            let total = Nutrition.calories(m, dr)
            guard total >= 1000 else { continue }   // skip partly logged days
            let by = Nutrition.calories(m.filter { minuteOf($0.time) <= minute }, dr.filter { minuteOf($0.time) <= minute })
            shares.append(Double(by) / Double(total))
        }
        let share = shares.count >= 5 ? shares.sorted()[shares.count / 2] : defaultShare(minuteOfDay: minute)

        let m = d.meals[day] ?? [], dr = d.drinks[day] ?? []
        let active = d.energy.log(day)?.isActiveDay ?? false
        let (out, basis) = projectedOut(d, active: active)
        let g = d.goals
        return DayPace(eatenShare: share, historyDays: shares.count >= 5 ? shares.count : 0,
                       caloriesIn: Nutrition.calories(m, dr), proteinIn: Nutrition.protein(m, dr),
                       projectedOut: out, outBasis: basis, deficit: deficit,
                       proteinTarget: g.flatMap { $0.proteinMax > 0 ? $0.proteinMin...max($0.proteinMin, $0.proteinMax) : nil },
                       fixedCalories: g.flatMap { g in
                           let lo = active ? g.caloriesActiveMin : g.caloriesRestMin, hi = active ? g.caloriesActiveMax : g.caloriesRestMax
                           return hi > 0 ? lo...max(lo, hi) : nil
                       })
    }

    /// Full-day expenditure: typical resting + the larger of activity so far and your typical activity on
    /// this kind of day (workout vs rest), from the last 14 completed days of Apple Health. Without Health
    /// data it's the app's usual estimate.
    static func projectedOut(_ d: Coach.Inputs, active: Bool) -> (Int?, String) {
        let day = d.today
        let past = (1...14).compactMap { i -> (rest: Int, act: Int, workout: Bool)? in
            guard let l = d.energy.log(day.adding(-i)), let r = l.restingEnergyKcal, r > 0, let a = l.activeEnergyKcal else { return nil }
            return (r, a, l.isActiveDay)
        }
        let soFar = d.energy.log(day)?.activeEnergyKcal
        guard past.count >= 3 else {
            let est = d.energy.out(day)
            return (est?.kcal, est.map { $0.fromHealth ? "Health activity so far + typical resting (no history yet)" : "estimate from BMR/weight" } ?? "unknown")
        }
        func median(_ xs: [Int]) -> Int { xs.sorted()[xs.count / 2] }
        let rest = median(past.map(\.rest))
        let same = past.filter { $0.workout == active }
        let typical = median((same.count >= 2 ? same : past).map(\.act))
        let act = max(soFar ?? 0, typical)
        let kind = same.count >= 2 ? (active ? "workout-day" : "rest-day") : "all-day"
        return (rest + act, "typical resting \(rest) + active \(act) (larger of so far \(soFar.map(String.init) ?? "n/a") and your typical \(kind) \(typical))")
    }
}

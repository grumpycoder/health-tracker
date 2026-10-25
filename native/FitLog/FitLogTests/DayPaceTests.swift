import XCTest
@testable import FitLog

/// Today check-in judgement for an unfinished day: pace from your own history, a budget from projected
/// expenditure minus the intended deficit, and status derived only from concerns the data supports.
final class DayPaceTests: XCTestCase {
    private let today = Day(y: 2026, m: 10, d: 25)
    private let deficit = 100...200

    private func meal(_ day: Day, _ hm: String, cal: Int, protein: Double) throws -> MealEntry {
        try Wire.decoder.decode(MealEntry.self, from: Data("""
        {"id":"\(UUID().uuidString)","time":"\(day.iso)T\(hm):00","mealType":1,"description":"Food","satiety":0,"calories":\(cal),"proteinG":\(protein)}
        """.utf8))
    }

    private func log(_ day: Day, type: Int, rest: Int?, active: Int?) throws -> DailyLog {
        try Wire.decoder.decode(DailyLog.self, from: Data("""
        {"date":"\(day.iso)","dayType":\(type),"restingEnergyKcal":\(rest.map(String.init) ?? "null"),"activeEnergyKcal":\(active.map(String.init) ?? "null")}
        """.utf8))
    }

    private let goals: GoalSettings = try! Wire.decoder.decode(GoalSettings.self, from: Data("""
    {"updatedAt":"2026-10-01T00:00:00","proteinMin":150,"proteinMax":170,"fatMin":0,"fatMax":0,"fiberMin":0,"fiberMax":0,
     "caloriesRestMin":1900,"caloriesRestMax":2100,"caloriesActiveMin":2200,"caloriesActiveMax":2400,
     "carbsRestMin":150,"carbsRestMax":200,"carbsActiveMin":200,"carbsActiveMax":250}
    """.utf8))

    /// 10 past days that eat lunch 12:30 (700 kcal / 50 g), dinner 18:30 (1100 / 70 g), snack 20:30 (300 / 20 g),
    /// alternating workout (700 active) and recovery (400 active) days, 1800 resting.
    private func inputs(todayType: Int, todayActiveSoFar: Int?, todayMeals: [(String, Int, Double)]) throws -> Coach.Inputs {
        var meals: [Day: [MealEntry]] = [:], logs: [DailyLog] = []
        for i in 1...10 {
            let d = today.adding(-i), workout = i % 2 == 0
            meals[d] = [try meal(d, "12:30", cal: 700, protein: 50), try meal(d, "18:30", cal: 1100, protein: 70),
                        try meal(d, "20:30", cal: 300, protein: 20)]
            logs.append(try log(d, type: workout ? 1 : 2, rest: 1800, active: workout ? 700 : 400))
        }
        meals[today] = try todayMeals.map { try meal(today, $0.0, cal: $0.1, protein: $0.2) }
        logs.append(try log(today, type: todayType, rest: nil, active: todayActiveSoFar))
        return Coach.Inputs(today: today, goals: goals, coachingGoals: "Modest deficit; hit protein.", meals: meals,
                            energy: EnergyHistory(logs: logs, body: [], sessions: [], today: today))
    }

    private func at(_ hm: String) -> Date { Wire.parse("\(today.iso)T\(hm):00")! }

    // The reported problem: 1:50 PM on a recovery day, lunch eaten, dinner and snack still to come.
    func testEarlyAfternoonRecoveryDay_isNormalProgress() throws {
        let d = try inputs(todayType: 2, todayActiveSoFar: 150, todayMeals: [("12:30", 700, 50)])
        let p = DayPace.make(d, now: at("13:50"), deficit: deficit)
        XCTAssertEqual(p.historyDays, 10)
        XCTAssertEqual(p.eatenShare, 700.0 / 2100, accuracy: 0.001, "a third of your usual day is eaten by now")
        XCTAssertEqual(p.projectedOut, 2200, "1800 resting + typical recovery-day active 400 (not the 150 so far)")
        XCTAssertEqual(p.calorieBudget, 2000...2100)
        XCTAssertEqual(p.caloriesRemaining, 1300...1400)
        XCTAssertEqual(p.proteinRemaining, 100)
        XCTAssertEqual(p.expectedCaloriesByNow, 683)
        XCTAssertFalse(p.late); XCTAssertFalse(p.deficitTooLarge); XCTAssertFalse(p.proteinUnlikely); XCTAssertFalse(p.overBudget)

        // A reply that still calls it low gets those concerns dropped → On track.
        let c = try XCTUnwrap(Coach.DailyCheck.parse([
            "synopsis": "Lunch logged; dinner to come.",
            "concerns": [["kind": "calories_low", "severity": "major", "evidence": "700 of 2000"],
                         ["kind": "protein_low", "severity": "concern", "evidence": "50 of 150 g"]],
            "tips": ["Aim for ~100 g protein across dinner and your snack."]], at: at("13:50"), pace: p))
        XCTAssertEqual(c.tone, "good")
        XCTAssertEqual(c.concerns, [])
        XCTAssertEqual(c.remaining, "Left today: 1,300–1,400 kcal · 100 g protein")

        let prompt = Coach.dailyPrompt(template: nil, d, now: at("13:50"), pace: p)
        XCTAssertTrue(prompt.contains("It is the afternoon on Sunday"))
        XCTAssertTrue(prompt.contains("usual share of the day's food eaten by now: 33% (median of your last 10 fully logged days)"))
        XCTAssertTrue(prompt.contains("end-of-day intake budget 2000-2100 kcal"))
        XCTAssertTrue(prompt.contains("remaining to minimum 100 g"))
        XCTAssertTrue(prompt.contains("flags: none — intake is normal progress for an unfinished day"))
        XCTAssertTrue(prompt.contains("fixed calorie range for today's day type 1900-2100 (reference only)"))
        XCTAssertFalse(prompt.contains("net so far"), "partial intake minus a full day's burn is what made 1:50 PM look like a huge deficit")
    }

    // Same day at 9:30 PM with dinner skipped: now the shortfalls are real.
    func testEveningWithLittleEaten_flagsRealShortfalls() throws {
        let d = try inputs(todayType: 2, todayActiveSoFar: 420, todayMeals: [("12:30", 700, 50), ("17:00", 400, 20)])
        let p = DayPace.make(d, now: at("21:30"), deficit: deficit)
        XCTAssertEqual(p.eatenShare, 1, accuracy: 0.001)
        XCTAssertTrue(p.late)
        XCTAssertEqual(p.projectedOut, 2220, "activity so far (420) beats the typical 400")
        XCTAssertEqual(p.caloriesRemaining, 920...1020)
        XCTAssertTrue(p.deficitTooLarge)
        XCTAssertTrue(p.proteinUnlikely, "80 g still to go at 9:30 PM")
        let c = try XCTUnwrap(Coach.DailyCheck.parse([
            "synopsis": "Light day.", "concerns": [["kind": "calories_low", "severity": "minor", "evidence": "1100 vs 2020"],
                                                   ["kind": "protein_low", "severity": "major", "evidence": "70 of 150 g"]],
            "tips": []], at: at("21:30"), pace: p))
        XCTAssertEqual(c.concerns?.count, 2)
        XCTAssertEqual(c.tone, "poor")
    }

    // Evening on track: nothing to flag even if the reply invents a concern.
    func testEveningOnTrack() throws {
        let d = try inputs(todayType: 2, todayActiveSoFar: 400,
                           todayMeals: [("12:30", 700, 50), ("18:30", 1100, 75), ("20:00", 250, 30)])
        let p = DayPace.make(d, now: at("21:00"), deficit: deficit)
        XCTAssertEqual(p.caloriesRemaining, 0...50)
        XCTAssertEqual(p.proteinRemaining, 0)
        XCTAssertFalse(p.deficitTooLarge || p.proteinUnlikely || p.overBudget)
        let c = try XCTUnwrap(Coach.DailyCheck.parse(["synopsis": "Right on plan.", "concerns": [], "tips": []], at: at("21:00"), pace: p))
        XCTAssertEqual(c.tone, "good")
        XCTAssertEqual(c.remaining, "Left today: 0–50 kcal · protein target met")
    }

    // Workout day: typical workout-day activity, or more when you've already burned more.
    func testWorkoutDay_usesWorkoutDayExpenditure() throws {
        let d = try inputs(todayType: 1, todayActiveSoFar: 900, todayMeals: [("12:30", 700, 50)])
        let p = DayPace.make(d, now: at("13:50"), deficit: deficit)
        XCTAssertEqual(p.projectedOut, 2700, "900 so far beats the typical workout-day 700")
        XCTAssertTrue(p.outBasis.contains("workout-day"))
        XCTAssertEqual(p.calorieBudget, 2500...2600)
        XCTAssertEqual(p.fixedCalories, 2200...2400, "workout-day range, as reference")
        let early = try inputs(todayType: 1, todayActiveSoFar: 200, todayMeals: [("12:30", 700, 50)])
        XCTAssertEqual(DayPace.make(early, now: at("13:50"), deficit: deficit).projectedOut, 2500, "typical workout-day 700 before the workout")
        XCTAssertTrue(Coach.dailyPrompt(template: nil, d, now: at("13:50"), pace: p).contains("(workout-day targets apply)"))
    }

    // Already past the whole day's budget at lunch: a real overshoot, kept.
    func testOverBudgetEarly_isKept() throws {
        let d = try inputs(todayType: 2, todayActiveSoFar: 150, todayMeals: [("12:30", 2300, 60)])
        let p = DayPace.make(d, now: at("13:50"), deficit: deficit)
        XCTAssertTrue(p.overBudget)
        let c = try XCTUnwrap(Coach.DailyCheck.parse([
            "synopsis": "Big lunch.", "concerns": [["kind": "calories_high", "severity": "minor", "evidence": "2300 vs 2100"]]], at: at("13:50"), pace: p))
        XCTAssertEqual(c.tone, "mixed")
    }

    // A recovery "concern" needs evidence in the data; without it, it's something to watch and the day stays On track.
    func testRecoveryConcern_withoutEvidence_isWatchOnly() throws {
        let d = try inputs(todayType: 2, todayActiveSoFar: 150, todayMeals: [("12:30", 700, 50)])
        let p = DayPace.make(d, now: at("13:50"), deficit: deficit)
        XCTAssertEqual(p.recoveryEvidence, [])
        let c = try XCTUnwrap(Coach.DailyCheck.parse(["synopsis": "On pace; HRV a touch low last night.",
            "concerns": [["kind": "recovery", "severity": "concern", "evidence": "HRV 48 vs 52"]]], at: at("13:50"), pace: p))
        XCTAssertEqual(c.tone, "good")
        XCTAssertEqual(c.watching.map(\.evidence), ["HRV 48 vs 52"], "kept, shown as something to watch")
    }

    // ...but a genuine recovery problem in the data still sets the status.
    func testRecoveryConcern_withEvidence_setsStatus() throws {
        var d = try inputs(todayType: 2, todayActiveSoFar: 150, todayMeals: [("12:30", 700, 50)])
        d.readinessToday = Readiness(score: 48, factors: [], flags: ["Wrist temperature +0.8 °C above your norm"], calibrating: false)
        let p = DayPace.make(d, now: at("13:50"), deficit: deficit)
        XCTAssertEqual(p.recoveryEvidence, ["readiness 48 (Low)", "Wrist temperature +0.8 °C above your norm"])
        let c = Coach.DailyCheck.parse(["synopsis": "Readiness is low with a temperature flag.",
            "concerns": [["kind": "recovery", "severity": "concern", "evidence": "readiness 48, temp +0.8"]]], at: at("13:50"), pace: p)
        XCTAssertEqual(c?.tone, "mixed")
        let fatigued = try Wire.decoder.decode(RecoveryEntry.self, from: Data("""
        {"id":"\(UUID().uuidString)","date":"\(today.iso)","recoveryRating":4,"fatigueRating":8,"sorenessSeverity":3,"notes":"heavy legs"}
        """.utf8))
        d.readinessToday = nil; d.checkIns = [fatigued]
        XCTAssertEqual(DayPace.recoveryEvidence(d), ["check-in fatigue 8/10"])
    }

    // Status agrees with the assessment: only watch items → On track; a "concern" → Mixed; "major" → Off track.
    func testStatusFollowsSeverity() {
        func c(_ sev: String) -> Coach.Concern { .init(kind: "other", severity: sev, evidence: "") }
        XCTAssertEqual(Coach.DailyCheck.status([]), "good")
        XCTAssertEqual(Coach.DailyCheck.status([c("watch"), c("watch")]), "good")
        XCTAssertEqual(Coach.DailyCheck.status([c("watch"), c("concern")]), "mixed")
        XCTAssertEqual(Coach.DailyCheck.status([c("concern"), c("major")]), "poor")
        XCTAssertEqual(Coach.DailyCheck.severity("minor"), "concern", "older replies")
    }

    // Fluids ahead of pace at 2 PM: a hydration tip isn't the lead, and with nothing wrong there are at most 2 tips.
    func testTips_onTrackDay_areProportionalAndOrdered() throws {
        var d = try inputs(todayType: 2, todayActiveSoFar: 150, todayMeals: [("12:30", 700, 50)])
        d.goals = try Wire.decoder.decode(GoalSettings.self, from: Data("""
        {"updatedAt":"2026-10-01T00:00:00","proteinMin":150,"proteinMax":170,"fatMin":0,"fatMax":0,"fiberMin":0,"fiberMax":0,
         "caloriesRestMin":1900,"caloriesRestMax":2100,"caloriesActiveMin":2200,"caloriesActiveMax":2400,
         "carbsRestMin":150,"carbsRestMax":200,"carbsActiveMin":200,"carbsActiveMax":250,
         "waterRestMin":100,"waterRestMax":128,"waterActiveMin":120,"waterActiveMax":150}
        """.utf8))
        d.drinks = [today: try (0..<3).map { _ in try Wire.decoder.decode(DrinkEntry.self, from: Data("""
        {"id":"\(UUID().uuidString)","time":"\(today.iso)T10:00:00","description":"Water","ounces":29}
        """.utf8)) }]
        let p = DayPace.make(d, now: at("14:00"), deficit: deficit)
        XCTAssertEqual(p.fluidsIn, 87)
        XCTAssertEqual(p.fluidsRemaining, 13)
        XCTAssertTrue(p.wellAhead("hydration"), "87 oz vs ~33 expected by 2 PM")
        XCTAssertFalse(p.wellAhead("protein"), "on pace, but 100 g still to go — worth a tip")
        XCTAssertTrue(Coach.paceSection(p).contains("fluids so far 87 oz, expected by now ≈ 33 oz, target 100-128 oz, remaining to minimum 13 oz (on or ahead of pace)"))
        let c = try XCTUnwrap(Coach.DailyCheck.parse([
            "synopsis": "On pace — 100 g protein still to go.",
            "concerns": [],
            "tips": [["text": "Keep sipping water.", "area": "hydration", "priority": "high"],
                     ["text": "Build dinner around ~60 g protein.", "area": "protein", "priority": "normal"],
                     ["text": "A short walk after dinner.", "area": "training", "priority": "normal"]]], at: at("14:00"), pace: p))
        XCTAssertEqual(c.tone, "good")
        XCTAssertEqual(c.tips, ["Build dinner around ~60 g protein.", "A short walk after dinner."],
                       "an area well ahead sinks to the end; 'high' means nothing with no concerns; capped at 2")
        XCTAssertEqual(c.remaining, "Left today: 1,300–1,400 kcal · 100 g protein · 13 oz fluids")
    }

    // With a real concern, its tip leads and up to 3 are kept; plain-string tips (older shape) still work.
    func testTips_withConcern_leadWithTheFix() throws {
        let d = try inputs(todayType: 2, todayActiveSoFar: 420, todayMeals: [("12:30", 700, 50), ("17:00", 400, 20)])
        let p = DayPace.make(d, now: at("21:30"), deficit: deficit)
        let c = try XCTUnwrap(Coach.DailyCheck.parse([
            "synopsis": "Protein well short with the day nearly done.",
            "concerns": [["kind": "protein_low", "severity": "concern", "evidence": "70 of 150 g at 9:30 PM"]],
            "tips": ["Note how you feel tomorrow.",
                     ["text": "A 40 g protein shake before bed.", "area": "protein", "priority": "high"],
                     ["text": "Plan tomorrow's lunch.", "area": "other", "priority": "normal"],
                     ["text": "Stretch.", "area": "recovery", "priority": "normal"]]], at: at("21:30"), pace: p))
        XCTAssertEqual(c.tone, "mixed")
        XCTAssertEqual(c.tips.first, "A 40 g protein shake before bed.")
        XCTAssertEqual(c.tips.count, 3)
    }

    func testWithoutHistory_usesDefaultEatingWindow() throws {
        var d = try inputs(todayType: 2, todayActiveSoFar: 150, todayMeals: [("12:30", 700, 50)])
        d.meals = [today: d.meals[today]!]
        let p = DayPace.make(d, now: at("13:50"), deficit: deficit)
        XCTAssertEqual(p.historyDays, 0)
        XCTAssertEqual(p.eatenShare, DayPace.defaultShare(minuteOfDay: 13 * 60 + 50), accuracy: 0.0001)
        XCTAssertEqual(p.eatenShare, 0.488, accuracy: 0.001)
    }
}

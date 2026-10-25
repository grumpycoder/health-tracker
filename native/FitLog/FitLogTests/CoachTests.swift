import XCTest
@testable import FitLog

final class CoachTests: XCTestCase {
    private let today = Day(y: 2026, m: 10, d: 25)

    private func meal(_ time: String, cal: Int, protein: Double) throws -> MealEntry {
        try Wire.decoder.decode(MealEntry.self, from: Data("""
        {"id":"\(UUID().uuidString)","time":"\(time)","mealType":1,"description":"Chicken","satiety":0,"calories":\(cal),"proteinG":\(protein)}
        """.utf8))
    }

    private func inputs() throws -> Coach.Inputs {
        let body = try Wire.decoder.decode(BodyMeasurement.self, from: Data("""
        {"date":"2026-10-01","weightLbs":200,"basalMetabolicRate":2000,"waistInches":38.5}
        """.utf8))
        let yesterday = try meal("2026-10-24T12:00:00", cal: 2000, protein: 160)
        let todays = try meal("2026-10-25T08:00:00", cal: 500, protein: 40)
        return Coach.Inputs(today: today, goals: nil, coachingGoals: "My primary goal is body recomposition.\n\n1. Muscle development",
                            meals: [today.adding(-1): [yesterday], today: [todays]],
                            energy: EnergyHistory(logs: [], body: [body], sessions: [], today: today), body: [body])
    }

    func testPromptCarriesGoalsVerbatimAndTheData() throws {
        let p = Coach.prompt(template: nil, try inputs())
        XCTAssertTrue(p.contains("USER'S COACHING GOALS (verbatim):\nMy primary goal is body recomposition.\n\n1. Muscle development"))
        // BMR 2000 × 1.25 = 2500 estimated out → yesterday's net −500.
        XCTAssertTrue(p.contains("2026-10-24 Sat | unset | 2000 | 2500 est | -500 | 160 |"), p)
        XCTAssertTrue(p.contains("2026-10-25 Sun (today, partial) | unset | 500 | 2500 est | ? |"), "no net for a partial day")
        XCTAssertTrue(p.contains("2026-10-23 Fri | unset | not logged"))
        XCTAssertTrue(p.contains("last 7 days -500 (1 days)"), "average uses completed, logged days only")
        XCTAssertTrue(p.contains("2026-10-01: weight 200, waist 38.5"))
        for banned in ["edication", "Lab", "essation"] { XCTAssertFalse(p.contains(banned), banned) }
    }

    func testReportParse() {
        let r = Coach.Report.parse(["status": "On-Track", "headline": "Deficit ~150/day, protein on target.",
                                    "sections": [["title": "Energy balance", "points": ["Avg net −150", ""]]], "actions": []], at: Date())
        XCTAssertEqual(r?.status, "on-track")
        XCTAssertEqual(r?.sections.first?.points, ["Avg net −150"])
        XCTAssertNil(Coach.Report.parse(["status": "adjust"], at: Date()), "nothing to show")
    }

    func testDailyPromptFocusesOnToday() throws {
        let now = Wire.parse("2026-10-25T14:30:00")!
        let p = Coach.dailyPrompt(template: nil, try inputs(), now: now)
        XCTAssertTrue(p.contains("CURRENT TIME OF DAY: the afternoon on Sunday"))
        XCTAssertTrue(p.contains("USER'S COACHING GOALS (verbatim)"))
        XCTAssertTrue(p.contains("TOTALS SO FAR: 500 kcal, 40 g protein"))
        XCTAssertTrue(p.contains("net so far -2000 kcal"))
        XCTAssertTrue(p.contains("7-DAY AVERAGE NET: -500 kcal/day over 1 logged days"))
        XCTAssertTrue(p.contains("2026-10-23 Fri | unset | not logged"))
        XCTAssertNil(Coach.DailyCheck.parse(["tone": "good"], at: now))
        XCTAssertEqual(Coach.DailyCheck.parse(["tone": "Good", "synopsis": "On track.", "tips": []], at: now)?.tone, "good")
    }
}

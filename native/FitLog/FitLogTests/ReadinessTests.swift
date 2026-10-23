import XCTest
@testable import FitLog

final class ReadinessTests: XCTestCase {
    private let today = Day(y: 2026, m: 10, d: 23)
    private func series(_ base: Double, jitter: Double = 2, days: ClosedRange<Int> = 4...33) -> [Day: Double] {
        Dictionary(uniqueKeysWithValues: days.map { (today.adding(-$0), base + ($0 % 2 == 0 ? jitter : -jitter)) })
    }

    func testAtBaselineWithGoodSleepIsModerateToReady() throws {
        var x = Readiness.Inputs()
        x.hrv = series(50).merging([today: 50, today.adding(-1): 50, today.adding(-2): 50]) { a, _ in a }
        x.restingHR = series(60).merging([today: 60]) { a, _ in a }
        x.sleepHours = 7.5; x.deepRemHours = 3; x.efficiency = 0.92
        let r = try XCTUnwrap(Readiness.compute(x, today: today))
        // HRV 50, RHR 50, sleep 100 → (50×.35 + 50×.20 + 100×.25) / .80 = 65.6
        XCTAssertEqual(r.score, 66)
        XCTAssertFalse(r.calibrating)
        XCTAssertEqual(r.recoveryRating, 7)
        XCTAssertEqual(r.fatigueRating, 4)
    }

    func testLowHrvHighRestingHrPullsScoreDown() throws {
        var x = Readiness.Inputs()
        x.hrv = series(50).merging([today: 40, today.adding(-1): 40, today.adding(-2): 40]) { a, _ in a }
        x.restingHR = series(60).merging([today: 66]) { a, _ in a }
        let r = try XCTUnwrap(Readiness.compute(x, today: today))
        XCTAssertLessThan(r.score, 20)
    }

    func testFeverSignalCapsScore() throws {
        var x = Readiness.Inputs()
        x.sleepHours = 8; x.deepRemHours = 3.2; x.efficiency = 0.95
        x.wristTemp = series(35.0, jitter: 0.1).merging([today: 35.8]) { a, _ in a }
        let r = try XCTUnwrap(Readiness.compute(x, today: today))
        XCTAssertEqual(r.score, 40)
        XCTAssertEqual(r.flags.count, 1)
    }

    func testShortBaselineIsCalibrating() throws {
        var x = Readiness.Inputs()
        x.hrv = series(50, days: 4...8).merging([today: 50]) { a, _ in a }
        XCTAssertTrue(try XCTUnwrap(Readiness.compute(x, today: today)).calibrating)
    }

    func testHeavyRecentLoadLowersLoadFactor() throws {
        var x = Readiness.Inputs()
        for d in 3..<28 where d % 3 == 0 { x.load[today.adding(-d)] = 60 }   // ~60 min every 3rd day
        x.load[today] = 90; x.load[today.adding(-1)] = 90; x.load[today.adding(-2)] = 90
        let f = try XCTUnwrap(Readiness.compute(x, today: today)?.factors.first { $0.name == "Training load" })
        XCTAssertEqual(f.score, 0)
    }
}

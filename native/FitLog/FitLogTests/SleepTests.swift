import HealthKit
import XCTest
@testable import FitLog

final class SleepTests: XCTestCase {
    private let t0 = Calendar.current.date(from: DateComponents(year: 2026, month: 10, day: 22, hour: 23))!
    private func s(_ value: Int, _ fromMin: Double, _ toMin: Double) -> HKCategorySample {
        HKCategorySample(type: HKCategoryType(.sleepAnalysis), value: value,
                         start: t0.addingTimeInterval(fromMin * 60), end: t0.addingTimeInterval(toMin * 60))
    }

    func testOverlappingStageBlocksCountOnce() throws {
        // Core 0–240 and an overlapping duplicate core 120–300 → 300 min, not 420.
        let n = try XCTUnwrap(HealthService.build(Day(y: 2026, m: 10, d: 23), "w", [s(3, 0, 240), s(3, 120, 300), s(4, 300, 360)]))
        XCTAssertEqual(n.asleep, 360 * 60)
        XCTAssertEqual(n.core, 300 * 60)
        XCTAssertEqual(n.deep, 60 * 60)
        XCTAssertTrue(n.hasStages)
    }

    func testAwakeningsAndEfficiency() throws {
        // in bed 0–480; asleep 10–200, awake 200–215, asleep 215–470.
        let n = try XCTUnwrap(HealthService.build(Day(y: 2026, m: 10, d: 23), "w",
                                                  [s(0, 0, 480), s(3, 10, 200), s(2, 200, 215), s(5, 215, 470)]))
        XCTAssertEqual(n.awakenings, 1)
        XCTAssertEqual(n.efficiency!, 445.0 / 480.0, accuracy: 0.0001)
    }

    func testScoreMatchesTheMauiFormula() throws {
        // 7.5 h asleep, 2 awakenings, 3 h deep+REM → 70 + 14 + 10 = 94.
        let n = try XCTUnwrap(HealthService.build(Day(y: 2026, m: 10, d: 23), "w",
            [s(3, 0, 90), s(2, 90, 100), s(4, 100, 190), s(2, 190, 200), s(5, 200, 290), s(3, 290, 470)]))
        XCTAssertEqual(n.hours, 7.5, accuracy: 0.001)
        XCTAssertEqual(n.awakenings, 2)
        XCTAssertEqual(n.estimatedScore, 94)
    }

    func testUnionMergesAdjacentAndOverlapping() {
        let d = Date()
        let u = HealthService.union([(d, d + 10), (d + 5, d + 20), (d + 20, d + 30), (d + 40, d + 50)])
        XCTAssertEqual(u.count, 2)
        XCTAssertEqual(u[0].1, d + 30)
    }
}

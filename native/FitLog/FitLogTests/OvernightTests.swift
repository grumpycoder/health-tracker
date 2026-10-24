import HealthKit
import XCTest
@testable import FitLog

final class OvernightTests: XCTestCase {
    private let t0 = Date(timeIntervalSince1970: 1_800_000_000)
    private func at(_ min: Double) -> Date { t0.addingTimeInterval(min * 60) }
    private func reading(_ min: Double, _ v: Double) -> Reading { Reading(start: at(min), end: at(min + 1), value: v) }
    private var spans: [(Date, Date)] { [(at(0), at(60)), (at(90), at(200))] }

    func testHrvUsesOnlyAsleepReadingsAsAGeometricMean() throws {
        // 75 min is in the awake gap; 2 ms is implausible.
        let h = Overnight.hrv([reading(30, 40), reading(75, 100), reading(150, 60), reading(160, 2)], asleep: spans)
        XCTAssertEqual(h.n, 2)
        XCTAssertEqual(try XCTUnwrap(h.value), (40.0 * 60).squareRoot(), accuracy: 1e-9)
        XCTAssertNil(Overnight.hrv([reading(75, 100)], asleep: spans).value)
    }

    func testHeartRateNeedsEnoughValidAsleepReadings() throws {
        let nine = (0..<9).map { reading(Double(100 + $0 * 5), 52) } + [reading(170, 250)]
        XCTAssertNil(Overnight.heartRate(nine, asleep: spans).value)
        XCTAssertEqual(Overnight.heartRate(nine, asleep: spans).n, 9)
        let ok = (0..<10).map { reading(Double(100 + $0 * 5), $0 < 5 ? 50 : 54) } + [reading(75, 90)]
        let hr = Overnight.heartRate(ok, asleep: spans)
        XCTAssertEqual(hr.n, 10)
        XCTAssertEqual(try XCTUnwrap(hr.value), 52, accuracy: 1e-9)
    }

    func testMedianAndSampleSd() {
        XCTAssertEqual(Overnight.median([3, 1, 2]), 2)
        XCTAssertEqual(Overnight.median([4, 1, 2, 3]), 2.5)
        XCTAssertEqual(Overnight.sd([2, 4, 4, 4, 5, 5, 7, 9]), 2.138, accuracy: 0.001)
    }

    // MARK: night assignment

    private func sample(_ value: Int, _ tz: String, _ y: Int, _ m: Int, _ d: Int, _ h: Int, _ min: Int, hours: Double) -> HKCategorySample {
        var cal = Calendar(identifier: .gregorian); cal.timeZone = TimeZone(identifier: tz)!
        let start = cal.date(from: DateComponents(year: y, month: m, day: d, hour: h, minute: min))!
        return HKCategorySample(type: HKCategoryType(.sleepAnalysis), value: value, start: start, end: start.addingTimeInterval(hours * 3600),
                                metadata: [HKMetadataKeyTimeZone: tz])
    }

    func testNightCrossingMidnightBelongsToTheWakeDay() throws {
        let ns = HealthService.mainNights([sample(3, "America/Chicago", 2026, 10, 22, 23, 0, hours: 8)])
        XCTAssertEqual(ns.map(\.day), [Day(y: 2026, m: 10, d: 23)])
    }

    func testAfternoonNapIsLeftOutOfTheNight() throws {
        let ns = HealthService.mainNights([sample(3, "America/Chicago", 2026, 10, 22, 23, 0, hours: 7.5),
                                           sample(3, "America/Chicago", 2026, 10, 23, 14, 0, hours: 1)])
        XCTAssertEqual(ns.count, 1)
        XCTAssertEqual(ns[0].hours, 7.5, accuracy: 1e-9)
    }

    func testEveningNapAloneIsNotANightButAShortRealNightIs() {
        XCTAssertTrue(HealthService.mainNights([sample(3, "America/Chicago", 2026, 10, 22, 19, 0, hours: 1)]).isEmpty)
        let short = HealthService.mainNights([sample(3, "America/Chicago", 2026, 10, 23, 2, 0, hours: 2.5)])
        XCTAssertEqual(short.map(\.day), [Day(y: 2026, m: 10, d: 23)])
    }

    func testRecordedTimeZoneDecidesTheDay() {
        // 11 PM–7 AM in Tokyo ends at 22:00 UTC the day before — still the Tokyo wake day.
        let ns = HealthService.mainNights([sample(3, "Asia/Tokyo", 2026, 10, 22, 23, 0, hours: 8)])
        XCTAssertEqual(ns.map(\.day), [Day(y: 2026, m: 10, d: 23)])
    }

    func testDaylightSavingNightCountsRealTime() throws {
        // US clocks fall back at 2 AM on Nov 1, 2026: 11 PM → 7 AM on the clock is 9 real hours.
        var cal = Calendar(identifier: .gregorian); cal.timeZone = TimeZone(identifier: "America/Chicago")!
        let wake = cal.date(from: DateComponents(year: 2026, month: 11, day: 1, hour: 7))!
        let start = cal.date(from: DateComponents(year: 2026, month: 10, day: 31, hour: 23))!
        let s = HKCategorySample(type: HKCategoryType(.sleepAnalysis), value: 3, start: start, end: wake, metadata: [HKMetadataKeyTimeZone: "America/Chicago"])
        let n = try XCTUnwrap(HealthService.mainNights([s]).first)
        XCTAssertEqual(n.day, Day(y: 2026, m: 11, d: 1))
        XCTAssertEqual(n.hours, 9, accuracy: 1e-9)
    }

    // MARK: Sleep tab ↔ readiness

    func testSleepTabAndReadinessShareTheOvernightHrv() throws {
        let today = Day(y: 2026, m: 10, d: 23)
        var nights: [SleepNight] = []
        for i in 0...30 {
            let d = today.adding(-i)
            let start = d.date.addingTimeInterval(-7 * 3600)
            var n = try XCTUnwrap(HealthService.build(d, "w", [HKCategorySample(type: HKCategoryType(.sleepAnalysis), value: 3, start: start, end: start.addingTimeInterval(7 * 3600))]))
            n.vitals.hrvMs = 40 + Double(i % 5); n.vitals.hrvSamples = 2
            n.vitals.sleepingHR = 55 + Double(i % 3); n.vitals.hrSamples = 100
            nights.append(n)
        }
        let byDay = Dictionary(uniqueKeysWithValues: nights.map { ($0.day, $0.metrics) })
        let x = Readiness.inputs(nights: nights.map(\.metrics), load: [:], vitalsSource: nil, today: today)
        XCTAssertEqual(x.nights.first?.hrvMs, nights[0].vitals.hrvMs, "readiness stores the Sleep tab's value for last night")
        let f = try XCTUnwrap(Readiness.compute(x, today: today)?.factors.first { $0.name == "Overnight HRV" })
        XCTAssertEqual(try XCTUnwrap(f.baseline), exp(try XCTUnwrap(Overnight.hrvBaseline(byDay, for: today)).center), accuracy: 1e-9)
    }
}

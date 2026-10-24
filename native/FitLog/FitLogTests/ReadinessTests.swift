import XCTest
@testable import FitLog

final class ReadinessTests: XCTestCase {
    private let today = Day(y: 2026, m: 10, d: 23)

    /// A night `ago` days before today. HRV alternates ×/÷ 1.04 and HR ±2 by parity so the
    /// baseline medians land exactly on `hrv` / `hr`.
    private func night(_ ago: Int, hrv: Double? = 50, hr: Double? = 55, jitter: Bool = true, hours: Double = 7.5,
                       deepRem: Double? = nil, eff: Double? = nil, hrvN: Int = 3, hrN: Int = 120) -> NightMetrics {
        let up = ago % 2 == 0
        let d = today.adding(-ago)
        return NightMetrics(day: d, source: "watch", bedtime: d.date.addingTimeInterval(-8 * 3600), wake: d.date.addingTimeInterval(-30 * 60),
                            sleepHours: hours, deepRemHours: deepRem, efficiency: eff,
                            hrvMs: hrv.map { jitter ? (up ? $0 * 1.04 : $0 / 1.04) : $0 }, hrvSamples: hrv == nil ? 0 : hrvN,
                            sleepingHR: hr.map { jitter ? (up ? $0 + 2 : $0 - 2) : $0 }, hrSamples: hr == nil ? 4 : hrN)
    }
    /// Last night exactly at the norm, then 30 jittered nights.
    private func history(last: NightMetrics? = nil) -> [NightMetrics] {
        [last ?? night(0, jitter: false, deepRem: 3, eff: 0.92)] + (1...30).map { night($0) }
    }
    private func inputs(_ nights: [NightMetrics], load: [Day: Double] = [:]) -> Readiness.Inputs {
        Readiness.inputs(nights: nights, load: load, vitalsSource: "Apple Watch", today: today)
    }
    private func factor(_ r: Readiness?, _ name: String) -> Readiness.Factor? { r?.factors.first { $0.name == name } }

    func testAtBaselineWithGoodSleepIsModerateToReady() throws {
        let r = try XCTUnwrap(Readiness.compute(inputs(history()), today: today))
        // At your norm = 75: HRV 75, HR 75, sleep 100 → (75×.35 + 75×.20 + 100×.25) / .80 = 82.8
        XCTAssertEqual(factor(r, "Overnight HRV")?.score, 75)
        XCTAssertEqual(factor(r, "Sleeping HR")?.score, 75)
        XCTAssertEqual(r.score, 83)
        XCTAssertFalse(r.calibrating)
        XCTAssertEqual(r.recoveryRating, 8)
        XCTAssertEqual(r.fatigueRating, 3)
        XCTAssertTrue(r.missing.isEmpty)
        XCTAssertTrue(r.issues.isEmpty)
    }

    func testWeightsUnchangedAndContributionsAddUp() throws {
        let r = try XCTUnwrap(Readiness.compute(inputs(history(), load: [today.adding(-2): 40, today.adding(-10): 40]), today: today))
        XCTAssertEqual(Dictionary(uniqueKeysWithValues: r.factors.map { ($0.name, $0.weight) }),
                       ["Overnight HRV": 0.35, "Sleeping HR": 0.20, "Sleep": 0.25, "Training load": 0.15])
        XCTAssertEqual(r.factors.map(\.share).reduce(0, +), 1, accuracy: 1e-9)
        XCTAssertEqual(r.factors.map(\.contribution).reduce(0, +), r.rawScore, accuracy: 1e-9)
        XCTAssertEqual(r.score, Int(r.rawScore.dotnetRounded))
    }

    func testLowHrvHighHeartRatePullsScoreDown() throws {
        var ns = history(last: night(0, hrv: 35, hr: 63, jitter: false))
        ns[1] = night(1, hrv: 35, jitter: false); ns[2] = night(2, hrv: 35, jitter: false)
        let r = try XCTUnwrap(Readiness.compute(inputs(ns), today: today))
        XCTAssertLessThan(r.score, 40, "well below norm on both should still read low")
    }

    func testMissingLastNightIsNotReplacedByAnOlderNight() throws {
        let ns = Array(history().dropFirst())   // newest is yesterday's wake day
        let r = try XCTUnwrap(Readiness.compute(inputs(ns, load: [today.adding(-1): 30]), today: today))
        XCTAssertEqual(r.factors.map(\.name), ["Training load"])
        XCTAssertEqual(Set(r.missing), ["HRV", "sleeping HR", "sleep"])
        XCTAssertTrue(r.issues.first?.contains("isn't in Apple Health yet") == true)
    }

    func testNoAsleepHrvLastNightLeavesHrvOutAndSaysSo() throws {
        let r = try XCTUnwrap(Readiness.compute(inputs(history(last: night(0, hrv: nil, jitter: false))), today: today))
        XCTAssertNil(factor(r, "Overnight HRV"))
        XCTAssertEqual(r.missing, ["HRV"])
        XCTAssertTrue(r.issues.contains { $0.contains("No HRV readings while asleep") })
        XCTAssertNotNil(factor(r, "Sleeping HR"))
    }

    func testHrvNeedsTwoOfTheLastThreeNights() throws {
        var ns = history()
        ns[1] = night(1, hrv: nil); ns[2] = night(2, hrv: nil)
        let r = try XCTUnwrap(Readiness.compute(inputs(ns), today: today))
        XCTAssertNil(factor(r, "Overnight HRV"))
        ns[2] = night(2)
        XCTAssertEqual(factor(Readiness.compute(inputs(ns), today: today), "Overnight HRV")?.nights, [today, today.adding(-2)])
    }

    func testTooFewHeartRateReadingsLeavesSleepingHrOut() throws {
        let r = try XCTUnwrap(Readiness.compute(inputs(history(last: night(0, hr: nil, jitter: false))), today: today))
        XCTAssertNil(factor(r, "Sleeping HR"))
        XCTAssertTrue(r.issues.contains { $0.contains("Only 4 heart-rate readings") })
    }

    func testHrvBaselineExcludesTheRollingNights() throws {
        let base = try XCTUnwrap(factor(Readiness.compute(inputs(history()), today: today), "Overnight HRV"))
        var ns = history(); ns[1] = night(1, hrv: 20, jitter: false)
        let changedRecent = try XCTUnwrap(factor(Readiness.compute(inputs(ns), today: today), "Overnight HRV"))
        XCTAssertEqual(changedRecent.baseline, base.baseline, "a rolling-window night must not move the baseline")
        XCTAssertNotEqual(changedRecent.value, base.value)
        ns = history(); ns[3] = night(3, hrv: 20, jitter: false)
        let changedBaseline = try XCTUnwrap(factor(Readiness.compute(inputs(ns), today: today), "Overnight HRV"))
        XCTAssertEqual(changedBaseline.value, base.value)
        XCTAssertEqual(changedBaseline.baselineN, 28)
    }

    func testMedianBaselineShrugsOffAnOutlierNight() throws {
        var ns = history(); ns[10] = night(10, hrv: 250, hr: 90, jitter: false)
        let r = try XCTUnwrap(Readiness.compute(inputs(ns), today: today))
        XCTAssertEqual(try XCTUnwrap(factor(r, "Overnight HRV")?.baseline), 50, accuracy: 2.1)
        XCTAssertEqual(try XCTUnwrap(factor(r, "Sleeping HR")?.baseline), 55, accuracy: 2.01)
    }

    func testIdenticalInputsGiveIdenticalResultsWhateverTheOrder() throws {
        let load: [Day: Double] = [today.adding(-1): 45, today.adding(-5): 30, today.adding(-12): 60]
        let a = inputs(history(), load: load)
        let b = inputs(history().shuffled(), load: load)
        XCTAssertEqual(a, b)
        XCTAssertEqual(Readiness.compute(a, today: today), Readiness.compute(b, today: today))
        let e = JSONEncoder(); e.outputFormatting = .sortedKeys   // as the snapshot store encodes
        XCTAssertEqual(try e.encode(a), try e.encode(b))
    }

    func testTodaysWorkoutDoesNotChangeTodaysScore() throws {
        var load: [Day: Double] = [:]
        for d in 1...28 where d % 2 == 0 { load[today.adding(-d)] = 30 }
        let before = try XCTUnwrap(Readiness.compute(inputs(history(), load: load), today: today)).score
        load[today] = 120   // a big session this morning
        XCTAssertEqual(try XCTUnwrap(Readiness.compute(inputs(history(), load: load), today: today)).score, before)
    }

    func testFeverSignalCapsScore() throws {
        var ns = history()
        for i in ns.indices { ns[i].wristTempC = i == 0 ? 35.8 : (i % 2 == 0 ? 35.1 : 34.9) }
        let r = try XCTUnwrap(Readiness.compute(inputs(ns), today: today))
        XCTAssertEqual(r.score, 40)
        XCTAssertEqual(r.flags.count, 1)
    }

    func testShortBaselineIsCalibrating() throws {
        let r = try XCTUnwrap(Readiness.compute(inputs(Array(history().prefix(8))), today: today))
        XCTAssertTrue(r.calibrating)
        XCTAssertEqual(factor(r, "Overnight HRV")?.baselineN, 5)
    }

    func testHeavyRecentLoadLowersLoadFactor() throws {
        var load: [Day: Double] = [:]
        for d in 3..<28 where d % 3 == 0 { load[today.adding(-d)] = 60 }   // ~60 min every 3rd day
        load[today.adding(-1)] = 90; load[today.adding(-2)] = 90; load[today.adding(-3)] = 90
        XCTAssertEqual(factor(Readiness.compute(inputs([], load: load), today: today), "Training load")?.score, 0)
    }

    // MARK: snapshots

    func testSnapshotRoundTripsAndReproducesTheScore() throws {
        let store = try RowStore()
        let x = inputs(history(), load: [today.adding(-2): 40])
        let r = try XCTUnwrap(Readiness.compute(x, today: today))
        let s = try XCTUnwrap(ReadinessSnapshot.next(after: nil, day: today, inputs: x, result: r, now: Date()))
        try store.saveReadinessSnapshot(s)
        let loaded = try XCTUnwrap(store.readinessSnapshot(today))
        XCTAssertEqual(loaded, s)
        XCTAssertEqual(loaded.recomputed(), loaded.result)
    }

    func testUnchangedRefreshDoesNotRewriteAndChangesKeepFirstTime() throws {
        let x = inputs(history())
        let r = try XCTUnwrap(Readiness.compute(x, today: today))
        let t0 = Date(timeIntervalSince1970: 1_000), t1 = Date(timeIntervalSince1970: 2_000)
        let s = try XCTUnwrap(ReadinessSnapshot.next(after: nil, day: today, inputs: x, result: r, now: t0))
        XCTAssertNil(ReadinessSnapshot.next(after: s, day: today, inputs: x, result: r, now: t1))
        let x2 = inputs(history(last: night(0, hr: 60, jitter: false)))
        let s2 = try XCTUnwrap(ReadinessSnapshot.next(after: s, day: today, inputs: x2, result: try XCTUnwrap(Readiness.compute(x2, today: today)), now: t1))
        XCTAssertEqual(s2.firstComputedAt, t0)
        XCTAssertEqual(s2.computedAt, t1)
        XCTAssertEqual(s2.revisions, 2)
    }

    func testOtherDaysAndOlderVersionsArePreserved() throws {
        let store = try RowStore()
        let x = inputs(history())
        let r = try XCTUnwrap(Readiness.compute(x, today: today))
        let old = ReadinessSnapshot(day: today, algorithmVersion: 1, firstComputedAt: Date(), computedAt: Date(), inputs: x, result: r)
        let yesterday = ReadinessSnapshot(day: today.adding(-1), algorithmVersion: Readiness.algorithmVersion, firstComputedAt: Date(), computedAt: Date(), inputs: x, result: r)
        try store.saveReadinessSnapshot(old); try store.saveReadinessSnapshot(yesterday)
        // An older-version row is never updated in place: the current version gets its own row.
        let fresh = try XCTUnwrap(ReadinessSnapshot.next(after: old, day: today, inputs: x, result: r, now: Date()))
        XCTAssertEqual(fresh.algorithmVersion, Readiness.algorithmVersion)
        try store.saveReadinessSnapshot(fresh)
        XCTAssertEqual(try store.readinessSnapshots().count, 3)
        XCTAssertEqual(try store.readinessSnapshot(today, version: 1), old)
        XCTAssertEqual(try store.readinessSnapshot(today.adding(-1)), yesterday)
    }
}

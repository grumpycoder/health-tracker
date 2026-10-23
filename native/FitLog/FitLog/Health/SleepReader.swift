import Foundation
import HealthKit

/// One night from Apple Health, keyed by the day you woke up. Built from ONE source per night (the
/// one with stage data and the most sleep — usually the Watch) so overlapping sources are never
/// added together, with each stage's intervals merged so overlaps within a source don't count twice.
struct SleepNight: Identifiable {
    enum Stage: Int, CaseIterable { case awake, rem, core, deep, unspecified
        var label: String { ["Awake", "REM", "Core", "Deep", "Asleep"][rawValue] }
    }
    struct Segment: Identifiable { let id = UUID(); let start: Date; let end: Date; let stage: Stage }

    var id: Day { day }
    let day: Day
    let source: String
    let bedtime: Date, wake: Date          // first → last sample of the chosen source
    let segments: [Segment]                // merged, sorted
    let asleep, deep, rem, core, awake: TimeInterval
    let awakenings: Int
    let hasStages: Bool
    var vitals = Vitals()

    struct Vitals {
        var hrvMs: Double?, restingHR: Double?, respiratoryRate: Double?, wristTempC: Double?, spo2: Double?
    }

    var hours: Double { asleep / 3600 }
    var inBed: TimeInterval { wake.timeIntervalSince(bedtime) }
    var efficiency: Double? { inBed > 0 ? min(1, asleep / inBed) : nil }

    /// The MAUI app's estimate (kept identical so synced scores stay comparable): duration vs 7.5h
    /// (70 pts), continuity (20, −3 per awakening), deep+REM share vs ~40% (10).
    var estimatedScore: Int {
        let duration = min(1, hours / 7.5) * 70
        let continuity = Double(max(0, 20 - awakenings * 3))
        let stages = hours > 0 ? min(1, (deep + rem) / 3600 / hours / 0.4) * 10 : 0
        return Int((duration + continuity + stages).dotnetRounded)
    }
}

extension HealthService {
    func readSleep(days: Int = 30) async -> [SleepNight] {
        guard isAvailable else { return [] }
        let start = Calendar.current.date(byAdding: .day, value: -days, to: Date())!
        let pred = HKQuery.predicateForSamples(withStart: start, end: nil)
        let q = HKSampleQueryDescriptor(predicates: [.categorySample(type: HKCategoryType(.sleepAnalysis), predicate: pred)],
                                        sortDescriptors: [SortDescriptor(\.startDate)])
        guard let samples = try? await q.result(for: healthStore) else { return [] }

        // Wake-day key: a sample ending in the evening belongs to the next morning's night.
        func night(_ s: HKCategorySample) -> Day {
            let end = s.endDate
            return Day(Calendar.current.component(.hour, from: end) >= 18 ? end.addingTimeInterval(86_400) : end)
        }
        var nights: [SleepNight] = []
        for (day, daySamples) in Dictionary(grouping: samples, by: night) {
            let bySource = Dictionary(grouping: daySamples) { $0.sourceRevision.source.bundleIdentifier }
            let candidates = bySource.compactMap { src, ss in Self.build(day, src, ss) }
            // Prefer stage-tracking sources, then the most sleep.
            if let best = candidates.max(by: { ($0.hasStages ? 1 : 0, $0.asleep) < ($1.hasStages ? 1 : 0, $1.asleep) }) {
                nights.append(best)
            }
        }
        nights.sort { $0.day > $1.day }
        for i in nights.indices { nights[i].vitals = await vitals(nights[i]) }
        return nights
    }

    nonisolated static func build(_ day: Day, _ source: String, _ ss: [HKCategorySample]) -> SleepNight? {
        func stage(_ v: Int) -> SleepNight.Stage? {
            switch v { case 2: .awake; case 5: .rem; case 3: .core; case 4: .deep; case 1: .unspecified; default: nil }   // 0 = in bed
        }
        var byStage: [SleepNight.Stage: [(Date, Date)]] = [:]
        for s in ss { if let st = stage(s.value) { byStage[st, default: []].append((s.startDate, s.endDate)) } }
        let merged = byStage.mapValues(Self.union)
        func total(_ st: SleepNight.Stage) -> TimeInterval { (merged[st] ?? []).reduce(0) { $0 + $1.1.timeIntervalSince($1.0) } }
        // Asleep = union of all asleep stages (a source can report overlapping stage blocks).
        let asleepSpans = Self.union([.rem, .core, .deep, .unspecified].flatMap { merged[$0] ?? [] })
        let asleep = asleepSpans.reduce(0) { $0 + $1.1.timeIntervalSince($1.0) }
        guard asleep > 0, let first = ss.map(\.startDate).min(), let last = ss.map(\.endDate).max() else { return nil }
        let hasStages = [.rem, .core, .deep, .awake].contains { merged[$0]?.isEmpty == false }
        // Awakenings: explicit awake periods inside the night, else 5–120 min gaps between asleep spans.
        let awakeIn = (merged[.awake] ?? []).filter { $0.0 > asleepSpans.first!.0 && $0.1 < asleepSpans.last!.1 }.count
        let gaps = zip(asleepSpans, asleepSpans.dropFirst()).filter { (5 * 60...120 * 60).contains($1.0.timeIntervalSince($0.1)) }.count
        let segments = merged.flatMap { st, spans in spans.map { SleepNight.Segment(start: $0.0, end: $0.1, stage: st) } }.sorted { $0.start < $1.start }
        return SleepNight(day: day, source: source, bedtime: first, wake: last, segments: segments, asleep: asleep,
                          deep: total(.deep), rem: total(.rem), core: total(.core), awake: total(.awake),
                          awakenings: max(awakeIn, gaps), hasStages: hasStages)
    }

    /// Merge overlapping/adjacent intervals.
    nonisolated static func union(_ spans: [(Date, Date)]) -> [(Date, Date)] {
        var out: [(Date, Date)] = []
        for s in spans.sorted(by: { $0.0 < $1.0 }) {
            if let last = out.last, s.0 <= last.1 { out[out.count - 1].1 = max(last.1, s.1) } else { out.append(s) }
        }
        return out
    }

    /// Averages over the night window (resting HR is the wake day's daily value).
    private func vitals(_ n: SleepNight) async -> SleepNight.Vitals {
        func avg(_ id: HKQuantityTypeIdentifier, _ unit: HKUnit, from: Date, to: Date) async -> Double? {
            let pred = HKSamplePredicate.quantitySample(type: HKQuantityType(id), predicate: HKQuery.predicateForSamples(withStart: from, end: to))
            let q = HKStatisticsQueryDescriptor(predicate: pred, options: .discreteAverage)
            return (try? await q.result(for: healthStore))?.averageQuantity()?.doubleValue(for: unit)
        }
        var v = SleepNight.Vitals()
        v.hrvMs = await avg(.heartRateVariabilitySDNN, .secondUnit(with: .milli), from: n.bedtime, to: n.wake)
        v.respiratoryRate = await avg(.respiratoryRate, .count().unitDivided(by: .minute()), from: n.bedtime, to: n.wake)
        v.spo2 = await avg(.oxygenSaturation, .percent(), from: n.bedtime, to: n.wake).map { $0 * 100 }
        v.wristTempC = await avg(.appleSleepingWristTemperature, .degreeCelsius(), from: n.bedtime.addingTimeInterval(-3600), to: n.wake.addingTimeInterval(3600))
        v.restingHR = await avg(.restingHeartRate, .count().unitDivided(by: .minute()), from: n.day.date, to: n.day.adding(1).date)
        return v
    }
}

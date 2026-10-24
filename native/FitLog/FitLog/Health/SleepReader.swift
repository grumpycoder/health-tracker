import Foundation
import HealthKit

/// One night from Apple Health, keyed by the day the main sleep ENDED (in the time zone it was
/// recorded in). Built from ONE source's main sleep session (the one with stage data and the most
/// sleep — usually the Watch) so overlapping sources are never added together and naps stay out,
/// with each stage's intervals merged so overlaps within a source don't count twice.
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
        /// Overnight HRV: geometric mean of Watch HRV readings taken while asleep (the readiness value).
        var hrvMs: Double?
        var hrvSamples = 0
        /// Mean Watch heart rate while asleep (the readiness heart-rate signal).
        var sleepingHR: Double?
        var hrSamples = 0
        /// Apple's daily resting HR for the wake day — shown on the Sleep tab, not used by readiness.
        var restingHR: Double?
        var respiratoryRate: Double?, wristTempC: Double?, spo2: Double?
    }

    var hours: Double { asleep / 3600 }
    var inBed: TimeInterval { wake.timeIntervalSince(bedtime) }
    var efficiency: Double? { inBed > 0 ? min(1, asleep / inBed) : nil }
    /// Merged asleep (non-awake) spans — the only time overnight HRV and heart rate are taken from.
    var asleepSpans: [(Date, Date)] { HealthService.union(segments.filter { $0.stage != .awake }.map { ($0.start, $0.end) }) }

    /// The numbers readiness and the Sleep tab share for this night.
    var metrics: NightMetrics {
        NightMetrics(day: day, source: source, bedtime: bedtime, wake: wake, sleepHours: hours,
                     deepRemHours: hasStages ? (deep + rem) / 3600 : nil, efficiency: efficiency,
                     hrvMs: vitals.hrvMs, hrvSamples: vitals.hrvSamples, sleepingHR: vitals.sleepingHR, hrSamples: vitals.hrSamples,
                     respiratory: vitals.respiratoryRate, wristTempC: vitals.wristTempC)
    }

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
    /// Apple Watch sources that have written `id` (bundle ids "com.apple.health.<device>"), so
    /// readiness uses one consistent measurement — e.g. HRV from the Watch, not mixed with Hume's.
    func watchSources(_ id: HKQuantityTypeIdentifier) async -> Set<HKSource> {
        let q = HKSourceQueryDescriptor(predicate: .quantitySample(type: HKQuantityType(id)))
        let all = (try? await q.result(for: healthStore)) ?? []
        return Set(all.filter { $0.bundleIdentifier.hasPrefix("com.apple.health.") })
    }

    /// Readings in [from, to) from `sources` (all sources when empty). Heart-rate readings the Watch
    /// tagged as taken while moving are dropped — they aren't resting-state values.
    func readings(_ id: HKQuantityTypeIdentifier, _ unit: HKUnit, from: Date, to: Date, sources: Set<HKSource>) async -> [Reading] {
        guard isAvailable else { return [] }
        var p: NSPredicate = HKQuery.predicateForSamples(withStart: from, end: to)
        if !sources.isEmpty { p = NSCompoundPredicate(andPredicateWithSubpredicates: [p, HKQuery.predicateForObjects(from: sources)]) }
        let q = HKSampleQueryDescriptor(predicates: [.quantitySample(type: HKQuantityType(id), predicate: p)], sortDescriptors: [SortDescriptor(\.startDate)])
        let ss = (try? await q.result(for: healthStore)) ?? []
        return ss.compactMap { s in
            if (s.metadata?[HKMetadataKeyHeartRateMotionContext] as? NSNumber)?.intValue == HKHeartRateMotionContext.active.rawValue { return nil }
            return Reading(start: s.startDate, end: s.endDate, value: s.quantity.doubleValue(for: unit))
        }
    }

    /// Nights for the Sleep tab and readiness (40 days covers readiness's 3-night window + 28-night baseline).
    func readSleep(days: Int = 40) async -> [SleepNight] {
        guard isAvailable else { return [] }
        let start = Calendar.current.date(byAdding: .day, value: -days, to: Date())!
        let pred = HKQuery.predicateForSamples(withStart: start, end: nil)
        let q = HKSampleQueryDescriptor(predicates: [.categorySample(type: HKCategoryType(.sleepAnalysis), predicate: pred)],
                                        sortDescriptors: [SortDescriptor(\.startDate)])
        guard let samples = try? await q.result(for: healthStore) else { return [] }
        var nights = Self.mainNights(samples)
        let hrvSources = await watchSources(.heartRateVariabilitySDNN), hrSources = await watchSources(.heartRate)
        for i in nights.indices { nights[i].vitals = await vitals(nights[i], hrvSources: hrvSources, hrSources: hrSources) }
        return nights
    }

    /// A break this long between samples ends one sleep session and starts another.
    nonisolated static let sessionGap: TimeInterval = 2 * 3600

    /// Sleep samples → one main night per wake day, newest first. Each source's samples are split
    /// into sessions at 2 h+ gaps; a session belongs to the day it ends (one ending after 6 PM to the
    /// next morning), in the time zone it was recorded in. Only a session that covers part of
    /// midnight–6 AM, or is 3 h+ asleep between 6 PM and noon, counts as a night — shorter
    /// daytime/evening sleeps are naps and are left out. Ties pick the same source every time.
    nonisolated static func mainNights(_ samples: [HKCategorySample]) -> [SleepNight] {
        func zone(_ s: HKCategorySample) -> TimeZone {
            (s.metadata?[HKMetadataKeyTimeZone] as? String).flatMap(TimeZone.init(identifier:)) ?? .current
        }
        func rank(_ n: SleepNight) -> (Int, Double, String) { (n.hasStages ? 1 : 0, n.asleep, n.source) }
        var best: [Day: SleepNight] = [:]
        for (src, ss) in Dictionary(grouping: samples, by: { $0.sourceRevision.source.bundleIdentifier }) {
            var sessions: [[HKCategorySample]] = []
            var end = Date.distantPast
            for s in ss.sorted(by: { ($0.startDate, $0.endDate) < ($1.startDate, $1.endDate) }) {
                if sessions.isEmpty || s.startDate.timeIntervalSince(end) >= sessionGap { sessions.append([s]) } else { sessions[sessions.count - 1].append(s) }
                end = max(end, s.endDate)
            }
            for sess in sessions {
                let last = sess.max { $0.endDate < $1.endDate }!
                let tz = zone(last)
                var cal = Calendar(identifier: .gregorian); cal.timeZone = tz
                let endDay = Day(last.endDate, in: tz)
                let day = cal.component(.hour, from: last.endDate) >= 18 ? endDay.adding(1) : endDay
                guard let n = build(day, src, sess) else { continue }
                let dayStart = cal.date(from: DateComponents(year: day.y, month: day.m, day: day.d))!
                let core = (dayStart, cal.date(byAdding: .hour, value: 6, to: dayStart)!)
                let window = (cal.date(byAdding: .hour, value: -6, to: dayStart)!, cal.date(byAdding: .hour, value: 12, to: dayStart)!)
                let spans = n.asleepSpans
                func overlap(_ w: (Date, Date)) -> TimeInterval {
                    spans.reduce(0) { $0 + max(0, min($1.1, w.1).timeIntervalSince(max($1.0, w.0))) }
                }
                guard overlap(core) > 0 || overlap(window) >= 3 * 3600 else { continue }   // a nap
                if let cur = best[day], rank(cur) >= rank(n) { continue }
                best[day] = n
            }
        }
        return best.values.sorted { $0.day > $1.day }
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

    /// Overnight HRV and heart rate from asleep segments only (Watch readings); the other vitals are
    /// averages over the whole night window, and resting HR is Apple's daily value for the wake day.
    private func vitals(_ n: SleepNight, hrvSources: Set<HKSource>, hrSources: Set<HKSource>) async -> SleepNight.Vitals {
        func avg(_ id: HKQuantityTypeIdentifier, _ unit: HKUnit, from: Date, to: Date) async -> Double? {
            let pred = HKSamplePredicate.quantitySample(type: HKQuantityType(id), predicate: HKQuery.predicateForSamples(withStart: from, end: to))
            let q = HKStatisticsQueryDescriptor(predicate: pred, options: .discreteAverage)
            return (try? await q.result(for: healthStore))?.averageQuantity()?.doubleValue(for: unit)
        }
        var v = SleepNight.Vitals()
        let spans = n.asleepSpans
        let hrv = Overnight.hrv(await readings(.heartRateVariabilitySDNN, .secondUnit(with: .milli), from: n.bedtime, to: n.wake, sources: hrvSources), asleep: spans)
        v.hrvMs = hrv.value; v.hrvSamples = hrv.n
        let hr = Overnight.heartRate(await readings(.heartRate, .count().unitDivided(by: .minute()), from: n.bedtime, to: n.wake, sources: hrSources), asleep: spans)
        v.sleepingHR = hr.value; v.hrSamples = hr.n
        v.respiratoryRate = await avg(.respiratoryRate, .count().unitDivided(by: .minute()), from: n.bedtime, to: n.wake)
        v.spo2 = await avg(.oxygenSaturation, .percent(), from: n.bedtime, to: n.wake).map { $0 * 100 }
        v.wristTempC = await avg(.appleSleepingWristTemperature, .degreeCelsius(), from: n.bedtime.addingTimeInterval(-3600), to: n.wake.addingTimeInterval(3600))
        v.restingHR = await avg(.restingHeartRate, .count().unitDivided(by: .minute()), from: n.day.date, to: n.day.adding(1).date)
        return v
    }
}

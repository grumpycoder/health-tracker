import Foundation
import HealthKit

/// Apple Health energy, read live (never written to the synced DailyLog — the MAUI app owns that
/// import during the side-by-side period, and two apps creating DailyLog rows for the same date
/// would collide on the server's unique Date index). Overlaid onto the synced logs in memory.
@MainActor
final class HealthService: ObservableObject {
    struct DayEnergy { var resting: Int?; var active: Int? }

    @Published private(set) var energy: [Day: DayEnergy] = [:]
    private let store = HKHealthStore()
    var healthStore: HKHealthStore { store }
    private let basal = HKQuantityType(.basalEnergyBurned)
    private let active = HKQuantityType(.activeEnergyBurned)
    private let bodyMass = HKQuantityType(.bodyMass)
    private let waist = HKQuantityType(.waistCircumference)

    var isAvailable: Bool { HKHealthStore.isHealthDataAvailable() }

    func requestAccessAndLoad() async {
        guard isAvailable else { return }
        do {
            try await store.requestAuthorization(toShare: [HKObjectType.workoutType(), active, bodyMass, waist], read: [basal, active, HKObjectType.workoutType(), bodyMass, waist, HKCategoryType(.sleepAnalysis),
                     HKQuantityType(.heartRateVariabilitySDNN), HKQuantityType(.restingHeartRate), HKQuantityType(.heartRate), HKQuantityType(.respiratoryRate),
                     HKQuantityType(.appleSleepingWristTemperature), HKQuantityType(.oxygenSaturation)])
            await load()
        } catch { }
    }

    /// Daily sums for the last 15 days including today (today's active energy is "so far").
    func load() async {
        guard isAvailable else { return }
        let cal = Calendar.current
        let end = cal.startOfDay(for: Date()).addingTimeInterval(86_400)
        let start = cal.date(byAdding: .day, value: -15, to: end)!
        async let rest = sums(basal, start, end)
        async let act = sums(active, start, end)
        let (r, a) = await (rest, act)
        var out: [Day: DayEnergy] = [:]
        for (d, v) in r { out[d, default: DayEnergy()].resting = v }
        for (d, v) in a { out[d, default: DayEnergy()].active = v }
        energy = out
    }

    private func sums(_ type: HKQuantityType, _ start: Date, _ end: Date) async -> [Day: Int] {
        let pred = HKSamplePredicate.quantitySample(type: type, predicate: HKQuery.predicateForSamples(withStart: start, end: end))
        let q = HKStatisticsCollectionQueryDescriptor(predicate: pred, options: .cumulativeSum,
                                                      anchorDate: start, intervalComponents: DateComponents(day: 1))
        guard let coll = try? await q.result(for: store) else { return [:] }
        var out: [Day: Int] = [:]
        coll.enumerateStatistics(from: start, to: end) { s, _ in
            if let kcal = s.sumQuantity()?.doubleValue(for: .kilocalorie()) { out[Day(s.startDate)] = Int(kcal.rounded(.toNearestOrEven)) }
        }
        return out
    }

    /// Weight (lb) / waist (in) samples from OTHER sources since `since`, latest per local day —
    /// this app's own samples are skipped so a mirrored value never comes back as an "import".
    func readBody(since: Date) async -> (weights: [Day: Double], waists: [Day: Double]) {
        guard isAvailable else { return ([:], [:]) }
        func read(_ t: HKQuantityType, _ unit: HKUnit) async -> [Day: Double] {
            let pred = HKQuery.predicateForSamples(withStart: since, end: nil)
            let q = HKSampleQueryDescriptor(predicates: [.quantitySample(type: t, predicate: pred)],
                                            sortDescriptors: [SortDescriptor(\.startDate, order: .reverse)])
            let mine = Bundle.main.bundleIdentifier
            var out: [Day: Double] = [:]
            for s in (try? await q.result(for: store)) ?? [] where s.sourceRevision.source.bundleIdentifier != mine {
                let d = Day(s.startDate)
                if out[d] == nil { out[d] = s.quantity.doubleValue(for: unit) }   // newest first → latest per day
            }
            return out
        }
        let w = await read(bodyMass, .pound())
        let x = await read(waist, .inch())
        return (w, x)
    }

    /// Mirror a measurement's weight/waist to Health, replacing this app's own sample for that day
    /// (so editing doesn't duplicate). Samples are stamped at noon local, like the MAUI app.
    func mirrorBody(day: Day, weightLbs: Double?, waistInches: Double?) async {
        guard isAvailable else { return }
        let noon = Calendar.current.date(bySettingHour: 12, minute: 0, second: 0, of: day.date)!
        let dayPred = HKQuery.predicateForSamples(withStart: day.date, end: day.adding(1).date)
        let own = NSCompoundPredicate(andPredicateWithSubpredicates: [dayPred, HKQuery.predicateForObjects(from: .default())])
        for (type, unit, value) in [(bodyMass, HKUnit.pound(), weightLbs), (waist, HKUnit.inch(), waistInches)] {
            guard let value else { continue }
            _ = try? await store.deleteObjects(of: type, predicate: own)
            try? await store.save(HKQuantitySample(type: type, quantity: HKQuantity(unit: unit, doubleValue: value), start: noon, end: noon))
        }
    }

    /// Save a strength workout with an estimated active-energy sample, like the MAUI app: skipped
    /// when a workout from another source (e.g. the Watch) overlaps — it already measured this.
    /// kcal/min = MET 5 × 3.5 × kg / 200 (6.0 when weight is unknown).
    func saveStrengthWorkout(start: Date, end: Date, name: String, weightLbs: Double?) async {
        guard isAvailable, end > start else { return }
        let overlap = HKQuery.predicateForSamples(withStart: start, end: end)
        let others = try? await HKSampleQueryDescriptor(predicates: [.workout(overlap)], sortDescriptors: [], limit: 10).result(for: store)
        let mine = Bundle.main.bundleIdentifier
        if others?.contains(where: { $0.sourceRevision.source.bundleIdentifier != mine }) == true { return }

        let config = HKWorkoutConfiguration(); config.activityType = .traditionalStrengthTraining
        let builder = HKWorkoutBuilder(healthStore: store, configuration: config, device: .local())
        do {
            try await builder.beginCollection(at: start)
            try? await builder.addMetadata([HKMetadataKeyWorkoutBrandName: name])
            let perMin = (weightLbs ?? 0) > 0 ? 5.0 * 3.5 * (weightLbs! * 0.453592) / 200 : 6.0
            let kcal = HKQuantity(unit: .kilocalorie(), doubleValue: end.timeIntervalSince(start) / 60 * perMin)
            try? await builder.addSamples([HKQuantitySample(type: active, quantity: kcal, start: start, end: end)])
            try await builder.endCollection(at: end)
            _ = try await builder.finishWorkout()
        } catch { }
        await load()
    }

    /// Synced logs with live Health energy laid over them, following the C# import's rules:
    /// completed days need both values; today carries activity so far only (resting is partial).
    func overlay(_ logs: [DailyLog], today: Day = .today) -> [DailyLog] {
        var byDay = Dictionary(logs.map { ($0.date, $0) }, uniquingKeysWith: { a, b in
            (a.updatedAt ?? .distantPast) > (b.updatedAt ?? .distantPast) ? a : b })
        for (day, e) in energy {
            guard let act = e.active else { continue }
            if day < today, e.resting ?? 0 <= 0 { continue }
            var log = byDay[day] ?? DailyLog(date: day)
            log.activeEnergyKcal = act
            log.restingEnergyKcal = day < today ? e.resting : nil
            byDay[day] = log
        }
        return Array(byDay.values)
    }
}

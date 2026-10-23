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
    private let basal = HKQuantityType(.basalEnergyBurned)
    private let active = HKQuantityType(.activeEnergyBurned)

    var isAvailable: Bool { HKHealthStore.isHealthDataAvailable() }

    func requestAccessAndLoad() async {
        guard isAvailable else { return }
        do {
            try await store.requestAuthorization(toShare: [], read: [basal, active])
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

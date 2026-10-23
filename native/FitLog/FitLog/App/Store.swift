import Combine
import Foundation

/// App data facade for the views: typed rows from RowStore (refreshed after every sync or edit),
/// live Health energy overlaid for the energy math, and sync status.
@MainActor
final class Store: ObservableObject {
    @Published private(set) var meals: [MealEntry] = []
    @Published private(set) var drinks: [DrinkEntry] = []
    @Published private(set) var logs: [DailyLog] = []
    @Published private(set) var body: [BodyMeasurement] = []
    @Published private(set) var sessions: [WorkoutSession] = []
    @Published private(set) var goals: GoalSettings?
    @Published var error: String?

    let rows: RowStore
    let sync: SyncEngine
    let health: HealthService
    private var bag: Set<AnyCancellable> = []

    init(auth: Auth) {
        do { rows = try RowStore.onDisk() } catch { fatalError("Local database failed to open: \(error)") }
        sync = SyncEngine(store: rows, auth: auth)
        health = HealthService()
        sync.onChange = { [weak self] in self?.reload() }
        // Views observe Store; forward the nested objects' changes (sync status, Health data).
        sync.objectWillChange.sink { [weak self] in self?.objectWillChange.send() }.store(in: &bag)
        health.objectWillChange.sink { [weak self] in self?.objectWillChange.send() }.store(in: &bag)
        reload()
    }

    var syncing: Bool { sync.syncing }
    var lastSync: Date? { sync.lastSync }

    func refresh(auth: Auth) async {
        await sync.sync()
        await health.load()
        error = sync.error
        reload()
    }

    func reload() {
        do {
            meals = try rows.decode("MealEntry")
            drinks = try rows.decode("DrinkEntry")
            logs = try rows.decode("DailyLog")
            body = try rows.decode("BodyMeasurement")
            sessions = try rows.decode("WorkoutSession")
            goals = (try rows.decode("GoalSettings") as [GoalSettings]).max { $0.updatedAt < $1.updatedAt }
        } catch { self.error = error.localizedDescription }
    }

    func meals(on day: Day) -> [MealEntry] { meals.filter { Day($0.time) == day }.sorted { $0.time > $1.time } }
    func drinks(on day: Day) -> [DrinkEntry] { drinks.filter { Day($0.time) == day }.sorted { $0.time > $1.time } }
    var energy: EnergyHistory { EnergyHistory(logs: health.overlay(logs), body: body, sessions: sessions) }
}

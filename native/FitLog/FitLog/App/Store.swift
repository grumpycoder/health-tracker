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
    @Published private(set) var library = FoodLibrary()
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
            library = FoodLibrary(meals: try live("MealEntry").map { $0.row }, drinks: try live("DrinkEntry").map { $0.row },
                                  savedFoods: try live("SavedFoodEntry"))
        } catch { self.error = error.localizedDescription }
    }

    /// Raw, non-deleted rows with their ids.
    private func live(_ type: String) throws -> [(id: String, row: Row)] {
        try rows.rows(type).compactMap { r in
            if case .bool(true) = r["isDeleted"] { return nil }
            guard let id = r.string("id") else { return nil }
            return (id.lowercased(), r)
        }
    }

    // MARK: edits — saved locally, then pushed a few seconds later (several quick edits → one sync)

    func meal(_ id: UUID) -> MealDraft? {
        let key = id.uuidString.lowercased()
        return (try? rows.row("MealEntry", key)).flatMap { $0 }.map { MealDraft(id: key, row: $0) }
    }

    func drink(_ id: UUID) -> DrinkDraft? {
        let key = id.uuidString.lowercased()
        return (try? rows.row("DrinkEntry", key)).flatMap { $0 }.map { DrinkDraft(id: key, row: $0) }
    }

    func save(_ d: MealDraft) { write { if let id = d.id { try rows.update("MealEntry", id, d.row) } else { try rows.insert("MealEntry", d.row) } } }
    func save(_ d: DrinkDraft) { write { if let id = d.id { try rows.update("DrinkEntry", id, d.row) } else { try rows.insert("DrinkEntry", d.row) } } }
    func deleteMeal(_ id: UUID) { write { try rows.delete("MealEntry", id.uuidString.lowercased()) } }
    func deleteDrink(_ id: UUID) { write { try rows.delete("DrinkEntry", id.uuidString.lowercased()) } }

    /// Create or update the saved food with this name (per-serving macros).
    func saveFood(_ item: MealItem) {
        let name = item.name.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        write {
            let fields = Macros.of(item).row.merging(["name": .string(name)]) { a, _ in a }
            if let id = library.savedIds[name.lowercased()] { try rows.update("SavedFoodEntry", id, fields) }
            else { try rows.insert("SavedFoodEntry", fields) }
        }
    }

    private var pendingSync: Task<Void, Never>?
    private func write(_ change: () throws -> Void) {
        do { try change() } catch { self.error = error.localizedDescription; return }
        reload()
        pendingSync?.cancel()
        pendingSync = Task { [weak self] in
            try? await Task.sleep(for: .seconds(3))
            guard !Task.isCancelled, let self else { return }
            while self.sync.syncing { try? await Task.sleep(for: .seconds(1)) }
            await self.sync.sync()
            self.error = self.sync.error
        }
    }

    func meals(on day: Day) -> [MealEntry] { meals.filter { Day($0.time) == day }.sorted { $0.time > $1.time } }
    func drinks(on day: Day) -> [DrinkEntry] { drinks.filter { Day($0.time) == day }.sorted { $0.time > $1.time } }
    var energy: EnergyHistory { EnergyHistory(logs: health.overlay(logs), body: body, sessions: sessions) }
}

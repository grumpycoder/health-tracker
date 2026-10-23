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
    // Derived once per reload (not per redraw): per-day lookups and the energy history.
    @Published private(set) var energy = EnergyHistory(logs: [], body: [], sessions: [])
    @Published private(set) var caloriesByDay: [Day: Int] = [:]
    private var mealsByDay: [Day: [MealEntry]] = [:]
    private var drinksByDay: [Day: [DrinkEntry]] = [:]
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
        reload()   // also rebuilds energy with the fresh Health data
    }

    /// Everything the views read, computed together off the main thread.
    private struct Snapshot {
        var meals: [MealEntry] = [], drinks: [DrinkEntry] = [], logs: [DailyLog] = []
        var body: [BodyMeasurement] = [], sessions: [WorkoutSession] = [], goals: GoalSettings?
        var mealsByDay: [Day: [MealEntry]] = [:], drinksByDay: [Day: [DrinkEntry]] = [:]
        var caloriesByDay: [Day: Int] = [:], library = FoodLibrary()

        init() {}
        init(_ rows: RowStore) throws {
            meals = try rows.decode("MealEntry")
            drinks = try rows.decode("DrinkEntry")
            logs = try rows.decode("DailyLog")
            body = try rows.decode("BodyMeasurement")
            sessions = try rows.decode("WorkoutSession")
            goals = (try rows.decode("GoalSettings") as [GoalSettings]).max { $0.updatedAt < $1.updatedAt }
            mealsByDay = Dictionary(grouping: meals) { Day($0.time) }.mapValues { $0.sorted { $0.time > $1.time } }
            drinksByDay = Dictionary(grouping: drinks) { Day($0.time) }.mapValues { $0.sorted { $0.time > $1.time } }
            caloriesByDay = Nutrition.caloriesByDay(meals, drinks)
            let saved = try rows.rows("SavedFoodEntry").compactMap { r -> (id: String, row: Row)? in
                if case .bool(true) = r["isDeleted"] { return nil }
                return r.string("id").map { ($0.lowercased(), r) }
            }
            library = FoodLibrary(meals: meals, drinks: drinks, savedFoods: saved)
        }
    }

    private var reloadTask: Task<Void, Never>?

    /// Re-read everything in the background, then publish on the main thread. Overlapping
    /// reloads collapse: only the newest result is applied.
    func reload() {
        reloadTask?.cancel()
        let rows = self.rows
        reloadTask = Task { [weak self] in
            let result = await Task.detached(priority: .userInitiated) { Result { try Snapshot(rows) } }.value
            guard !Task.isCancelled, let self else { return }
            switch result {
            case .success(let s): self.apply(s)
            case .failure(let e): self.error = e.localizedDescription
            }
        }
    }

    private func apply(_ s: Snapshot) {
        meals = s.meals; drinks = s.drinks; logs = s.logs; body = s.body; sessions = s.sessions; goals = s.goals
        mealsByDay = s.mealsByDay; drinksByDay = s.drinksByDay; caloriesByDay = s.caloriesByDay; library = s.library
        rebuildEnergy()
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

    func save(_ d: MealDraft) { upsert("MealEntry", d.id, d.row) }
    func save(_ d: DrinkDraft) { upsert("DrinkEntry", d.id, d.row) }

    /// Insert, or update with only the fields that actually changed — so a save doesn't rewrite
    /// values the user didn't touch (e.g. trimming a time's fractional seconds).
    private func upsert(_ type: String, _ id: String?, _ fields: Row) {
        write {
            guard let id, let current = try rows.row(type, id) else { try rows.insert(type, fields); return }
            let changed = fields.filter { k, v in !Store.same(current[k], v, key: k) }
            if !changed.isEmpty { try rows.update(type, id, changed) }
        }
    }

    /// Same value as stored? Numbers compare by value (16 == 16.0); times to the second.
    nonisolated static func same(_ old: JSON?, _ new: JSON, key: String) -> Bool {
        let o = old ?? .null
        switch (o, new) {
        case let (.int(a), .double(b)), let (.double(b), .int(a)): return Double(a) == b
        case let (.string(a), .string(b)) where key == "time":
            guard let da = Wire.parse(a), let db = Wire.parse(b) else { return a == b }
            return abs(da.timeIntervalSince(db)) < 1
        default: return o == new
        }
    }
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

    func meals(on day: Day) -> [MealEntry] { mealsByDay[day] ?? [] }
    func drinks(on day: Day) -> [DrinkEntry] { drinksByDay[day] ?? [] }
    private func rebuildEnergy() { energy = EnergyHistory(logs: health.overlay(logs), body: body, sessions: sessions) }
}

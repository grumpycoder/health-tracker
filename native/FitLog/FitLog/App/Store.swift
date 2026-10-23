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
    @Published private(set) var exercises: [ExerciseDefinition] = []
    @Published private(set) var routines: [WorkoutRoutine] = []
    @Published private(set) var routineExercises: [RoutineExercise] = []
    @Published private(set) var sets: [ExerciseSet] = []
    @Published private(set) var feedback: [ExerciseFeedback] = []
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
    let ai: AIClient
    private var bag: Set<AnyCancellable> = []

    init(auth: Auth) {
        do { rows = try RowStore.onDisk() } catch { fatalError("Local database failed to open: \(error)") }
        sync = SyncEngine(store: rows, auth: auth)
        health = HealthService()
        ai = AIClient(auth: auth)
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
        var exercises: [ExerciseDefinition] = [], routines: [WorkoutRoutine] = [], routineExercises: [RoutineExercise] = []
        var sets: [ExerciseSet] = [], feedback: [ExerciseFeedback] = []
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
            exercises = (try rows.decode("ExerciseDefinition") as [ExerciseDefinition]).sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
            routines = (try rows.decode("WorkoutRoutine") as [WorkoutRoutine]).sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
            routineExercises = (try rows.decode("RoutineExercise") as [RoutineExercise]).sorted { $0.order < $1.order }
            sets = try rows.decode("ExerciseSet")
            feedback = try rows.decode("ExerciseFeedback")
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
        exercises = s.exercises; routines = s.routines; routineExercises = s.routineExercises; sets = s.sets; feedback = s.feedback
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

    /// The user's synced override for a prompt key (Settings → prompts in the other apps), if any.
    func promptOverride(_ key: String) -> String? {
        (try? rows.rows("PromptTemplate"))?.first {
            if case .bool(true) = $0["isDeleted"] { return false }
            return $0.string("promptKey") == key && !($0.string("text") ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }?.string("text")
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

    var latestWeightLbs: Double? { body.filter { $0.weightLbs != nil }.max { $0.date < $1.date }?.weightLbs }

    /// Several inserts for one workout; pushed together afterwards.
    func saveWorkout(_ change: (RowStore) throws -> Void) -> Bool {
        var ok = true
        write { do { try change(rows) } catch { ok = false; throw error } }
        return ok
    }

    /// Mark `day` a Workout day, like the C# WorkoutCompleted handler: create the DailyLog if the
    /// day has none, or set Unset → Workout; any other day type is left alone. DailyLog.Date is
    /// unique on the server, so pull first — if the other app already made today's row, update it
    /// instead of inserting a second one (which would make the whole push fail).
    func markWorkoutDay(_ day: Day) async {
        await sync.sync()
        let rowsForDay = ((try? rows.rows("DailyLog")) ?? []).filter {
            if case .bool(true) = $0["isDeleted"] { return false }
            return $0.string("date").map { $0.hasPrefix(Self.dayString(day)) } ?? false
        }
        if let existing = rowsForDay.first, let id = existing.string("id") {
            if (existing.int("dayType") ?? 0) == 0 { write { try rows.update("DailyLog", id, ["dayType": .int(1)]) } }
        } else {
            write { try rows.insert("DailyLog", ["date": Wire.day(day), "dayType": .int(1), "waterOz": .int(0)]) }
        }
    }

    // MARK: routines & library (same rules as the C# Routine aggregate / RoutineService)

    @discardableResult
    func createRoutine(_ name: String) -> String? {
        let n = name.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { return nil }
        var id: String?
        write { id = try rows.insert("WorkoutRoutine", ["name": .string(n), "archived": .bool(false), "executionMode": .int(0)]) }
        return id
    }

    func updateRoutine(_ id: UUID, _ fields: Row) { write { try rows.update("WorkoutRoutine", id.uuidString.lowercased(), fields) } }

    /// Refused when sessions reference it (the C# service does the same) — archive instead.
    func deleteRoutine(_ id: UUID) -> Bool {
        guard !sessions.contains(where: { $0.routineId == id }) else { return false }
        write {
            for slot in routineExercises where slot.routineId == id { try rows.delete("RoutineExercise", slot.id.uuidString.lowercased()) }
            try rows.delete("WorkoutRoutine", id.uuidString.lowercased())
        }
        return true
    }

    /// Pick the library exercise by name (case-insensitive — the server's Name index is unique), or create it.
    func exerciseNamed(_ name: String, measure: ExerciseMeasure) throws -> (id: String, measure: ExerciseMeasure) {
        let n = name.trimmingCharacters(in: .whitespaces)
        if let e = exercises.first(where: { $0.name.caseInsensitiveCompare(n) == .orderedSame }) { return (e.id.uuidString.lowercased(), e.measure) }
        let id = try rows.insert("ExerciseDefinition", ["name": .string(n), "measure": .int(Int64(measure.rawValue)), "retired": .bool(false)])
        return (id, measure)
    }

    /// Add an exercise to a routine at the end, with the MAUI defaults (3 sets, 10 reps or 30 s, 60 s rest).
    func addExercise(_ name: String, measure: ExerciseMeasure, to routine: UUID) {
        guard !name.trimmingCharacters(in: .whitespaces).isEmpty else { return }
        write {
            let ex = try exerciseNamed(name, measure: measure)
            let next = (routineExercises.filter { $0.routineId == routine }.map(\.order).max() ?? 0) + 1
            try rows.insert("RoutineExercise", [
                "routineId": .string(routine.uuidString.lowercased()), "exerciseDefinitionId": .string(ex.id), "order": .i(next),
                "targetSets": .int(3), "targetReps": ex.measure == .reps ? .int(10) : .null,
                "targetDurationSeconds": ex.measure == .duration ? .int(30) : .null, "restSeconds": .int(60),
                "targetWeight": .null, "targetNote": .null,
            ])
        }
    }

    func updateSlot(_ id: UUID, _ fields: Row) { write { try rows.update("RoutineExercise", id.uuidString.lowercased(), fields) } }

    /// Save a new order (or a removal) and renumber 1..N contiguously, like Routine.Normalize.
    func reorder(_ routine: UUID, _ ordered: [RoutineExercise], removing: [UUID] = []) {
        write {
            for id in removing { try rows.delete("RoutineExercise", id.uuidString.lowercased()) }
            for (i, slot) in ordered.enumerated() where slot.order != i + 1 {
                try rows.update("RoutineExercise", slot.id.uuidString.lowercased(), ["order": .i(i + 1)])
            }
        }
    }

    // MARK: body measurements

    func measurementRow(_ id: UUID) -> Row? { (try? rows.row("BodyMeasurement", id.uuidString.lowercased())) ?? nil }

    /// Insert or update (changed fields only); weight/waist are then mirrored to Health.
    func saveMeasurement(id: UUID?, day: Day, fields: Row) {
        upsert("BodyMeasurement", id?.uuidString.lowercased(), fields)
        let w = fields.double("weightLbs"), x = fields.double("waistInches")
        if w != nil || x != nil { Task { await health.mirrorBody(day: day, weightLbs: w, waistInches: x) } }
    }

    func deleteMeasurement(_ id: UUID) { write { try rows.delete("BodyMeasurement", id.uuidString.lowercased()) } }

    /// Import a year of weight/waist from other Health sources, like the MAUI "Pull from Apple
    /// Health": a new row per day that has none; on an existing day, only fill a missing waist.
    func pullBodyFromHealth() async -> String {
        let (weights, waists) = await health.readBody(since: Date().addingTimeInterval(-365 * 86_400))
        guard !weights.isEmpty || !waists.isEmpty else {
            return "Nothing to import. Only entries from other sources (Health app, scale, Watch) are pulled — this app's own are skipped."
        }
        var added = 0, filled = 0
        let byDay = Dictionary(body.compactMap { m in m.id.map { (m.date, ($0, m)) } }, uniquingKeysWith: { a, _ in a })
        write {
            for day in Set(weights.keys).union(waists.keys) {
                if let (id, m) = byDay[day] {
                    if m.waistInches == nil, let w = waists[day] {
                        try rows.update("BodyMeasurement", id.uuidString.lowercased(), ["waistInches": .d(w.dotnetRounded(2))]); filled += 1
                    }
                    continue
                }
                try rows.insert("BodyMeasurement", ["date": Wire.day(day), "weightLbs": .d(weights[day]?.dotnetRounded(1)),
                                                    "waistInches": .d(waists[day]?.dotnetRounded(2))])
                added += 1
            }
        }
        return added + filled == 0 ? "Already up to date." : "Imported \(added) day(s)\(filled > 0 ? ", filled waist on \(filled)" : "") from Health."
    }

    // MARK: history

    func updateSession(_ id: UUID, _ fields: Row, sets: [(UUID, Row)]) {
        write {
            try rows.update("WorkoutSession", id.uuidString.lowercased(), fields)
            for (sid, f) in sets { try rows.update("ExerciseSet", sid.uuidString.lowercased(), f) }
        }
    }

    func deleteSession(_ id: UUID) { write { try rows.delete("WorkoutSession", id.uuidString.lowercased()) } }

    nonisolated static func dayString(_ d: Day) -> String { String(format: "%04d-%02d-%02d", d.y, d.m, d.d) }

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

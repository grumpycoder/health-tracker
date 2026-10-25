import Combine
import Foundation
import WidgetKit

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
    @Published private(set) var sleepEntries: [SleepEntry] = []
    /// Rich nights read live from Apple Health (phone only — not synced).
    @Published private(set) var nights: [SleepNight] = []
    @Published private(set) var recovery: [RecoveryEntry] = []
    @Published private(set) var readiness: Readiness?
    /// Stored daily readiness calculations (local only), newest first.
    @Published private(set) var readinessHistory: [ReadinessSnapshot] = []
    @Published private(set) var library = FoodLibrary()
    /// The last coaching review (kept on this device until the next one).
    @Published private(set) var coachReport: Coach.Report?
    @Published private(set) var coaching = false
    /// The last Today check-in (shown only on the day it was made).
    @Published private(set) var dailyCheck: Coach.DailyCheck?
    /// Notes, newest first.
    @Published private(set) var notes: [NoteEntry] = []
    @Published private(set) var medSchedules: [MedicationSchedule] = []
    /// Logged doses, newest first.
    @Published private(set) var doses: [MedicationEntry] = []
    @Published private(set) var reminderSettings: [ReminderSetting] = []
    /// Latest day each routine reminder's task was done (key → day), for skip-when-done.
    private var reminderDone: [String: Day] = [:]
    /// Foods hidden from suggestions (lowercased), on this device only.
    @Published private(set) var hiddenFoods = Set(UserDefaults.standard.stringArray(forKey: "hiddenFoods") ?? [])
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
    let reminders = ReminderNotifier()
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
        reminders.objectWillChange.sink { [weak self] in self?.objectWillChange.send() }.store(in: &bag)
        dailyCheck = (try? rows.meta("dailyCheck")).flatMap { $0 }.flatMap { try? JSONDecoder().decode(Coach.DailyCheck.self, from: Data($0.utf8)) }
        coachReport = (try? rows.meta("coachReport")).flatMap { $0 }.flatMap { try? JSONDecoder().decode(Coach.Report.self, from: Data($0.utf8)) }
        reload()
    }

    var syncing: Bool { sync.syncing }
    var lastSync: Date? { sync.lastSync }

    func refresh(auth: Auth) async {
        drainQuickLogs()
        await sync.sync()
        await health.load()
        error = sync.error
        reload()   // also rebuilds energy with the fresh Health data
        nights = await health.readSleep()
        await reloadTask?.value   // readiness uses the freshly loaded sessions/feedback
        await computeReadiness()
        if sync.error == nil {   // only after a successful pull, so we see the other app's rows
            importSleep()
            autoFillRecovery()
        }
    }

    /// Everything the views read, computed together off the main thread.
    private struct Snapshot {
        var meals: [MealEntry] = [], drinks: [DrinkEntry] = [], logs: [DailyLog] = []
        var body: [BodyMeasurement] = [], sessions: [WorkoutSession] = [], goals: GoalSettings?
        var exercises: [ExerciseDefinition] = [], routines: [WorkoutRoutine] = [], routineExercises: [RoutineExercise] = []
        var sets: [ExerciseSet] = [], feedback: [ExerciseFeedback] = [], sleep: [SleepEntry] = [], recovery: [RecoveryEntry] = []
        var mealsByDay: [Day: [MealEntry]] = [:], drinksByDay: [Day: [DrinkEntry]] = [:]
        var caloriesByDay: [Day: Int] = [:], library = FoodLibrary()
        var medSchedules: [MedicationSchedule] = [], doses: [MedicationEntry] = [], reminderSettings: [ReminderSetting] = []
        var reminderDone: [String: Day] = [:]
        var notes: [NoteEntry] = []

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
            sleep = (try rows.decode("SleepEntry") as [SleepEntry]).sorted { $0.date > $1.date }
            recovery = (try rows.decode("RecoveryEntry") as [RecoveryEntry]).sorted { $0.date > $1.date }
            mealsByDay = Dictionary(grouping: meals) { Day($0.time) }.mapValues { $0.sorted { $0.time > $1.time } }
            drinksByDay = Dictionary(grouping: drinks) { Day($0.time) }.mapValues { $0.sorted { $0.time > $1.time } }
            caloriesByDay = Nutrition.caloriesByDay(meals, drinks)
            let saved = try rows.rows("SavedFoodEntry").compactMap { r -> (id: String, row: Row)? in
                if case .bool(true) = r["isDeleted"] { return nil }
                return r.string("id").map { ($0.lowercased(), r) }
            }
            library = FoodLibrary(meals: meals, drinks: drinks, savedFoods: saved)
            notes = (try rows.decode("NoteEntry") as [NoteEntry]).sorted { $0.time > $1.time }
            medSchedules = (try rows.decode("MedicationSchedule") as [MedicationSchedule]).sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
            doses = (try rows.decode("MedicationEntry") as [MedicationEntry]).sorted { $0.takenAt > $1.takenAt }
            reminderSettings = (try rows.decode("ReminderSetting") as [ReminderSetting]).sorted { $0.order < $1.order }
            // What each routine reminder checks for (the MAUI MainLayout skip-when-done rules).
            func latest(_ type: String, _ key: String, where ok: (Row) -> Bool = { _ in true }) throws -> Day? {
                try rows.rows(type).filter { r in
                    if case .bool(true) = r["isDeleted"] { return false }
                    return ok(r)
                }.compactMap { $0.string(key).flatMap { s in try? JSONDecoder().decode(Day.self, from: Data("\"\(s)\"".utf8)) } }.max()
            }
            reminderDone["measurement"] = body.map(\.date).max()
            reminderDone["progressPhoto"] = try latest("BodyMeasurement", "date") { !($0.string("photoPath") ?? "").isEmpty }
            reminderDone["labCheck"] = try latest("LabResult", "date")
            reminderDone["weeklyReview"] = try latest("WeeklyReview", "weekStart")
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
        sleepEntries = s.sleep
        recovery = s.recovery
        mealsByDay = s.mealsByDay; drinksByDay = s.drinksByDay; caloriesByDay = s.caloriesByDay; library = s.library
        library.hidden = hiddenFoods
        notes = s.notes
        medSchedules = s.medSchedules; doses = s.doses; reminderSettings = s.reminderSettings; reminderDone = s.reminderDone
        rebuildEnergy()
        rescheduleReminders()
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

    /// Tags you've used on meals — the vocabulary the AI is told to prefer.
    var tagVocabulary: [String] {
        Array(Set(meals.compactMap(\.tags).flatMap { $0.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) } }
            .filter { !$0.isEmpty })).sorted()
    }

    /// Macro targets + today's context, in the same shape as the C# prompt helpers.
    func plateContext() -> String {
        var t = ""
        if let g = goals {
            t += "DAILY MACRO/HYDRATION TARGETS (ranges are goals, not hard limits — a day slightly under/over is fine; flag only consistent misses). Calories, carbs, and water have separate rest-day and workout-day ranges — use the one matching today's day type:\n"
            if g.proteinMax > 0 { t += "  - Protein: \(g.proteinMin)-\(g.proteinMax) g\n" }
            if g.fatMax > 0 { t += "  - Fat: \(g.fatMin)-\(g.fatMax) g\n" }
            if g.fiberMax > 0 { t += "  - Fiber: \(g.fiberMin)-\(g.fiberMax) g\n" }
            if let a = g.addedSugarMax { t += "  - Added sugar: stay under \(a) g (less is better)\n" }
            t += "  - Calories: rest-day \(g.caloriesRestMin)-\(g.caloriesRestMax), workout-day \(g.caloriesActiveMin)-\(g.caloriesActiveMax)\n"
            t += "  - Carbohydrates: rest-day \(g.carbsRestMin)-\(g.carbsRestMax) g, workout-day \(g.carbsActiveMin)-\(g.carbsActiveMax) g\n"
        }
        let now = Date()
        let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX"); f.dateFormat = "yyyy-MM-dd HH:mm (EEEE)"
        t += "NOW: \(f.string(from: now))\n"
        let dt = ["(not set)", "Workout", "Recovery", "ActiveRecovery", "HighWorkload"]
        t += "PLANNED DAY TYPE: \(dt[min(4, max(0, energy.log(.today)?.dayType ?? 0))])\n"
        t += "MEALS TODAY:\n"
        let today = meals(on: .today).sorted { $0.time < $1.time }
        if today.isEmpty { t += "(none yet)\n" }
        let hm = DateFormatter(); hm.dateFormat = "HH:mm"
        for m in today {
            t += "  \(hm.string(from: m.time)) \(m.mealType.label) \"\(m.description)\"" + (m.calories.map { " \($0) cal" } ?? "")
                + (m.proteinG.map { ", \(Int($0))g protein" } ?? "") + (m.carbsG.map { ", \(Int($0))g carbs" } ?? "") + "\n"
        }
        let d = drinks(on: .today), m = meals(on: .today)
        t += "TODAY'S TOTALS SO FAR: \(Nutrition.calories(m, d)) cal, \(Int(Nutrition.protein(m, d)))g protein, \(Int(Nutrition.carbs(m, d)))g carbs, \(Int(Nutrition.fat(m, d)))g fat, \(Int(Nutrition.addedSugar(m, d)))g added sugar, \(Int(Nutrition.sodium(m, d)))mg sodium\n"
        return t
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
    /// Quick-add a water drink (same shape as the other apps' quick-add: a "Water" DrinkEntry).
    func logWater(_ oz: Int, at time: Date = Date()) { write { try rows.insert("DrinkEntry", ["time": Wire.local(time), "description": .string("Water"), "ounces": .i(oz)]) } }
    func deleteMeal(_ id: UUID) { write { try rows.delete("MealEntry", id.uuidString.lowercased()) } }
    func deleteDrink(_ id: UUID) { write { try rows.delete("DrinkEntry", id.uuidString.lowercased()) } }

    /// Create or update the saved food with this name (per-serving macros).
    func saveFood(_ item: MealItem) {
        let name = item.name.foodNameCased
        guard !name.isEmpty else { return }
        write {
            let fields = Macros.of(item).row.merging(["name": .string(name)]) { a, _ in a }
            if let id = library.savedIds[name.lowercased()] { try rows.update("SavedFoodEntry", id, fields) }
            else { try rows.insert("SavedFoodEntry", fields) }
        }
    }

    /// Hide a food from suggestions, or show it again. Its logs and saved macros are untouched.
    func toggleHidden(_ name: String) {
        let k = name.trimmingCharacters(in: .whitespaces).lowercased()
        if hiddenFoods.remove(k) == nil { hiddenFoods.insert(k) }
        UserDefaults.standard.set(hiddenFoods.sorted(), forKey: "hiddenFoods")
        library.hidden = hiddenFoods
    }

    /// Meals/drinks that use `name` (as their name or an item) — shown before a rename.
    func foodUseCount(_ name: String) -> Int {
        ["MealEntry", "DrinkEntry"].reduce(0) { n, type in
            n + ((try? rows.rows(type)) ?? []).filter { r in
                if case .bool(true) = r["isDeleted"] { return false }
                return FoodRename.changes(r, from: name, to: name + "\u{0}") != nil
            }.count
        }
    }

    /// Rename a food in every meal and drink that uses it (a merge if the new name exists).
    @discardableResult
    func renameFood(_ old: String, to new: String, macros: Macros? = nil) -> Int {
        mergeFoods([old], into: new, macros: macros)
    }

    /// Merge `names` into `keeper`: every meal and drink using one of them (as its name or an item)
    /// is renamed, and one saved-macros entry is kept — the keeper's own if it has one, else the most
    /// recently edited of the others — with the rest removed. `macros`, when given, are saved for the
    /// keeper. An existing keeper keeps its own hidden setting; a brand-new name (a plain rename)
    /// inherits the old one's. Returns how many logs changed.
    @discardableResult
    func mergeFoods(_ names: [String], into keeper: String, macros: Macros? = nil) -> Int {
        let target = keeper.foodNameCased
        let olds = names.map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty && $0 != target }
        guard !target.isEmpty, !olds.isEmpty else { return 0 }
        let keeperExists = library.allNames.contains { FoodRename.matches($0, target) && !olds.contains($0) }
        var changed = 0
        write {
            for type in ["MealEntry", "DrinkEntry"] {
                for r in try rows.rows(type) {
                    if case .bool(true) = r["isDeleted"] { continue }
                    guard let id = r.string("id") else { continue }
                    var row = r, all: Row = [:]
                    for old in olds {
                        guard let c = FoodRename.changes(row, from: old, to: target) else { continue }
                        for (k, v) in c { row[k] = v; all[k] = v }
                    }
                    if !all.isEmpty { try rows.update(type, id, all); changed += 1 }
                }
            }
            // Saved macros read fresh from the store (not the cached library), so chained edits agree.
            let wanted = [target] + olds
            let saved = try rows.rows("SavedFoodEntry").filter { r in
                if case .bool(true) = r["isDeleted"] { return false }
                return wanted.contains { FoodRename.matches(r.string("name"), $0) }
            }
            let own = saved.first { $0.string("name").map { FoodRename.matches($0, target) } ?? false }
            let keep = own ?? saved.max { ($0.string("updatedAt") ?? "") < ($1.string("updatedAt") ?? "") }
            for r in saved where r.string("id") != keep?.string("id") { if let id = r.string("id") { try rows.delete("SavedFoodEntry", id) } }
            if let id = keep?.string("id") { try rows.update("SavedFoodEntry", id, ["name": .string(target)].merging(macros?.row ?? [:]) { a, _ in a }) }
            else if let macros { try rows.insert("SavedFoodEntry", macros.row.merging(["name": .string(target)]) { a, _ in a }) }
        }
        let keys = olds.map { $0.lowercased() }
        if keys.contains(where: hiddenFoods.contains) {
            let wasHidden = keys.contains(where: hiddenFoods.contains)
            hiddenFoods.subtract(keys)
            if !keeperExists && wasHidden { hiddenFoods.insert(target.lowercased()) }
            UserDefaults.standard.set(hiddenFoods.sorted(), forKey: "hiddenFoods")
            library.hidden = hiddenFoods
        }
        return changed
    }

    /// Drop a food's saved macros so it goes back to its most recent log (no-op if it has none).
    func resetFood(_ name: String) {
        guard let id = library.savedIds[name.trimmingCharacters(in: .whitespaces).lowercased()] else { return }
        write { try rows.delete("SavedFoodEntry", id) }
    }

    var latestWeightLbs: Double? { body.filter { $0.weightLbs != nil }.max { $0.date < $1.date }?.weightLbs }

    /// Several inserts for one workout; pushed together afterwards.
    func saveWorkout(_ change: (RowStore) throws -> Void) -> Bool {
        var ok = true
        write { do { try change(rows) } catch { ok = false; throw error } }
        return ok
    }

    /// Mark `day` a Workout day, like the C# WorkoutCompleted handler: create the DailyLog if the
    /// day has none, or set Unset/Recovery → Workout (a day is Recovery until a workout says
    /// otherwise); Active Recovery / High Workload are left alone. DailyLog.Date is
    /// unique on the server, so pull first — if the other app already made today's row, update it
    /// instead of inserting a second one (which would make the whole push fail).
    func markWorkoutDay(_ day: Day) async {
        await sync.sync()
        let rowsForDay = ((try? rows.rows("DailyLog")) ?? []).filter {
            if case .bool(true) = $0["isDeleted"] { return false }
            return $0.string("date").map { $0.hasPrefix(Self.dayString(day)) } ?? false
        }
        if let existing = rowsForDay.first, let id = existing.string("id") {
            if [0, 2].contains(existing.int("dayType") ?? 0) { write { try rows.update("DailyLog", id, ["dayType": .int(1)]) } }
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

    /// Create a library exercise; nil + message when the name is taken (the server's Name index is
    /// unique, case-insensitive, among live rows).
    func createExercise(_ name: String, measure: ExerciseMeasure) -> String? {
        let n = name.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { return "Enter a name." }
        if exercises.contains(where: { $0.name.caseInsensitiveCompare(n) == .orderedSame }) { return "“\(n)” already exists." }
        write { _ = try rows.insert("ExerciseDefinition", ["name": .string(n), "measure": .int(Int64(measure.rawValue)), "retired": .bool(false)]) }
        return nil
    }

    /// Edit a library exercise (name, video, muscles, retired). Measure can't change — history depends on it.
    func updateExercise(_ id: UUID, name: String, videoUrl: String, muscles: [String], retired: Bool) -> String? {
        let n = name.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { return "Enter a name." }
        if exercises.contains(where: { $0.id != id && $0.name.caseInsensitiveCompare(n) == .orderedSame }) {
            return "“\(n)” already exists — pick a different name."
        }
        let v = videoUrl.trimmingCharacters(in: .whitespaces)
        upsert("ExerciseDefinition", id.uuidString.lowercased(), [
            "name": .string(n), "videoUrl": .s(v.isEmpty ? nil : v),
            "muscleGroups": .s(muscles.isEmpty ? nil : muscles.joined(separator: ",")), "retired": .bool(retired),
        ])
        return nil
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

    // MARK: sleep

    /// Create a synced SleepEntry for each Health night that has none yet (never overwrites — a
    /// manual entry or the other app's import wins), with the same fields/score as the MAUI import.
    private func importSleep() {
        let have = Set(((try? rows.rows("SleepEntry")) ?? []).compactMap { r -> String? in
            if case .bool(true) = r["isDeleted"] { return nil }
            return r.string("date").map { String($0.prefix(10)) }
        })
        // This morning's night only once it's surely over (after 11 AM) — imports never overwrite,
        // so a half-night imported at 3 AM would stick.
        let todayDone = Calendar.current.component(.hour, from: Date()) >= 11
        let missing = nights.filter { !have.contains(Self.dayString($0.day)) && ($0.day < .today || ($0.day == .today && todayDone)) }
        guard !missing.isEmpty else { return }
        write {
            for n in missing {
                try rows.insert("SleepEntry", [
                    "date": Wire.day(n.day), "durationHours": .d(n.hours.dotnetRounded(1)),
                    "interruptions": n.hasStages ? .i(n.awakenings) : .null,
                    "sleepScore": n.hasStages ? .i(n.estimatedScore) : .null,
                    "scoreEstimated": .bool(n.hasStages),
                    "notes": n.hasStages ? .string("Score estimated by Fit Log — not Apple's Sleep Score") : .null,
                ])
            }
        }
    }

    // MARK: readiness

    func computeReadiness() async {
        let today = Day.today
        let source = await health.watchSources(.heartRateVariabilitySDNN).isEmpty ? nil : "Apple Watch"
        let x = Readiness.inputs(nights: nights.map(\.metrics), load: Readiness.load(sessions: sessions, feedback: feedback),
                                 vitalsSource: source, today: today)
        readiness = Readiness.compute(x, today: today)
        if let r = readiness {
            do {
                if let s = ReadinessSnapshot.next(after: try rows.readinessSnapshot(today), day: today, inputs: x, result: r, now: Date()) {
                    try rows.saveReadinessSnapshot(s)
                }
            } catch { self.error = "Couldn't save today's readiness details: \(error.localizedDescription)" }
        }
        readinessHistory = (try? rows.readinessSnapshots()) ?? []
        publishWidget()
    }

    /// Today's stored calculation (current algorithm), for the details screen.
    var todaysReadinessSnapshot: ReadinessSnapshot? {
        readinessHistory.first { $0.day == .today && $0.algorithmVersion == Readiness.algorithmVersion }
    }

    var todaysRecovery: RecoveryEntry? { recovery.first { $0.date == .today } }

    /// Today's synced RecoveryEntry from readiness: created when the day has none, kept current
    /// while it's still the auto one; a check-in you save yourself is never overwritten.
    private func autoFillRecovery() {
        guard let r = readiness else { return }
        let today = ((try? rows.rows("RecoveryEntry")) ?? []).filter {
            if case .bool(true) = $0["isDeleted"] { return false }
            return $0.string("date").map { $0.hasPrefix(Self.dayString(.today)) } ?? false
        }
        let note = "\(RecoveryEntry.autoMarker) \(r.score)" + (r.calibrating ? " (calibrating)" : "")
        let fields: Row = ["recoveryRating": .i(r.recoveryRating), "fatigueRating": .i(r.fatigueRating), "notes": .string(note)]
        if today.isEmpty {
            write { try rows.insert("RecoveryEntry", fields.merging(["date": Wire.day(.today), "sorenessSeverity": .int(0)]) { a, _ in a }) }
        } else if today.count == 1, let row = today.first, let id = row.string("id"), (row.string("notes") ?? "").hasPrefix(RecoveryEntry.autoMarker) {
            upsert("RecoveryEntry", id, fields)
        }
    }

    /// Your check-in for a day: replaces the auto values (and the auto marker) for good.
    func saveCheckIn(day: Day, recovery rec: Int, fatigue: Int, soreness: [String], severity: Int, notes: String) {
        let n = notes.trimmingCharacters(in: .whitespacesAndNewlines)
        let fields: Row = ["date": Wire.day(day), "recoveryRating": .i(rec), "fatigueRating": .i(fatigue),
                           "sorenessLocations": .s(soreness.isEmpty ? nil : soreness.joined(separator: ",")),
                           "sorenessSeverity": .i(severity), "notes": .s(n.isEmpty ? nil : n)]
        upsert("RecoveryEntry", recovery.first { $0.date == day }?.id.uuidString.lowercased(), fields)
    }

    func sleepRow(_ id: UUID) -> Row? { (try? rows.row("SleepEntry", id.uuidString.lowercased())) ?? nil }

    /// Edit a night's synced summary. Entering a score by hand marks it as real (not estimated).
    func saveSleep(_ id: UUID, hours: Double?, score: Int?, interruptions: Int?, notes: String) {
        let n = notes.trimmingCharacters(in: .whitespacesAndNewlines)
        let wasScore = sleepEntries.first { $0.id == id }?.sleepScore
        var f: Row = ["durationHours": .d(hours), "sleepScore": .i(score), "interruptions": .i(interruptions), "notes": .s(n.isEmpty ? nil : n)]
        if score != wasScore { f["scoreEstimated"] = .bool(false) }
        upsert("SleepEntry", id.uuidString.lowercased(), f)
    }

    func deleteSleep(_ id: UUID) { write { try rows.delete("SleepEntry", id.uuidString.lowercased()) } }

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

    // MARK: coach

    /// Run a coaching review over the current data; returns an error message, or nil on success.
    func runCoach() async -> String? {
        coaching = true
        defer { coaching = false }
        let data = coachInputs()
        let template = promptOverride(Coach.promptKey)
        let prompt = await Task.detached { Coach.prompt(template: template, data) }.value
        do {
            guard let r = try await ai.coach(prompt) else { return "The AI's reply couldn't be read. Try again." }
            coachReport = r
            if let json = try? JSONEncoder().encode(r) { try? rows.setMeta("coachReport", String(decoding: json, as: UTF8.self)) }
            return nil
        } catch { return error.localizedDescription }
    }

    /// Today's check-in: how the day is going and what to do with the rest of it.
    func runDailyCheck() async -> String? {
        coaching = true
        defer { coaching = false }
        let data = coachInputs(), now = Date()
        let template = promptOverride(Coach.dailyKey)
        let prompt = await Task.detached { Coach.dailyPrompt(template: template, data, now: now) }.value
        do {
            guard let r = try await ai.dailyCheck(prompt) else { return "The AI's reply couldn't be read. Try again." }
            dailyCheck = r
            if let json = try? JSONEncoder().encode(r) { try? rows.setMeta("dailyCheck", String(decoding: json, as: UTF8.self)) }
            return nil
        } catch { return error.localizedDescription }
    }

    var todaysCheck: Coach.DailyCheck? { dailyCheck.flatMap { Day($0.generatedAt) == .today ? $0 : nil } }

    private func coachInputs() -> Coach.Inputs {
        Coach.Inputs(
            today: .today, goals: goals, coachingGoals: goalsRow()?.row.string("coachingGoals"),
            meals: mealsByDay, drinks: drinksByDay, energy: energy, sessions: sessions,
            routines: Dictionary(routines.map { ($0.id, $0.name) }, uniquingKeysWith: { a, _ in a }),
            exercises: Dictionary(exercises.map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a }),
            sets: sets, feedback: feedback, nights: nights.map(\.metrics),
            readiness: Dictionary(readinessHistory.filter { $0.algorithmVersion == Readiness.algorithmVersion }.map { ($0.day, $0.result.score) },
                                  uniquingKeysWith: { a, _ in a }),
            checkIns: recovery, body: body, readinessToday: readiness, notesToday: notes(on: .today))
    }

    // MARK: notes

    func notes(on day: Day) -> [NoteEntry] { notes.filter { Day($0.time) == day } }

    func addNote(_ text: String, at time: Date = Date()) {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { return }
        write { try rows.insert("NoteEntry", ["time": Wire.local(time), "text": .string(t)]) }
    }

    func updateNote(_ id: UUID, text: String) {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { return }
        upsert("NoteEntry", id.uuidString.lowercased(), ["text": .string(t)])
    }

    func deleteNote(_ id: UUID) { write { try rows.delete("NoteEntry", id.uuidString.lowercased()) } }

    // MARK: goals

    /// The synced goals singleton: the latest live row (same rule as the other apps).
    func goalsRow() -> (id: String, row: Row)? {
        let live = ((try? rows.rows("GoalSettings")) ?? []).filter { if case .bool(true) = $0["isDeleted"] { false } else { true } }
        guard let r = live.max(by: { ($0.date("updatedAt") ?? .distantPast) < ($1.date("updatedAt") ?? .distantPast) }),
              let id = r.string("id") else { return nil }
        return (id, r)
    }

    /// Changed fields only, so includeCessationData (not edited on the phone) is never touched.
    func saveGoals(_ fields: Row) { upsert("GoalSettings", goalsRow()?.id, fields) }

    // MARK: medications & reminders (same rules as the MAUI Medication / Reminders pages)

    func rescheduleReminders() {
        reminders.update(Meds.plan(schedules: medSchedules, doses: doses, settings: reminderSettings, lastDone: reminderDone))
    }

    var dueDoses: [MedicationSchedule] { Meds.outstanding(medSchedules, doses: doses) }

    func medScheduleRow(_ id: UUID) -> Row? { (try? rows.row("MedicationSchedule", id.uuidString.lowercased())) ?? nil }

    /// New schedules get a random notification id, like the MAUI page.
    func saveMedSchedule(id: UUID?, fields: Row) {
        var f = fields
        if id == nil { f["notificationId"] = .i(Int.random(in: 1...Int(Int32.max))); f["active"] = .bool(true) }
        upsert("MedicationSchedule", id?.uuidString.lowercased(), f)
    }

    func deleteMedSchedule(_ id: UUID) { write { try rows.delete("MedicationSchedule", id.uuidString.lowercased()) } }

    /// One-tap log from a schedule: its name and dose, now, at `site` for injections. Returns the dose id.
    @discardableResult
    func logDose(_ s: MedicationSchedule, site: String? = nil) -> UUID? {
        var id: String?
        write {
            id = try rows.insert("MedicationEntry", ["name": .string(s.name), "dose": .s(s.dose), "frequency": .string(s.repeat.label),
                                                     "takenAt": Wire.local(Date()), "injectionSite": .s(site), "reactionNotes": .null])
        }
        return id.flatMap(UUID.init(uuidString:))
    }

    /// The Log action on a dose reminder (the app may have just been woken in the background). Reads
    /// the schedule straight from the database — a cold launch may not have reloaded yet — skips it if
    /// that day's dose was already logged, then pushes right away rather than after the usual delay.
    func logDoseFromReminder(_ scheduleId: UUID, day: Day, site: String?) async {
        let schedules: [MedicationSchedule] = (try? rows.decode("MedicationSchedule")) ?? []
        guard let s = schedules.first(where: { $0.id == scheduleId }) else { return }
        let doses: [MedicationEntry] = (try? rows.decode("MedicationEntry")) ?? []
        if let last = Meds.lastTaken(doses)[s.name.lowercased()], last >= day { return }
        logDose(s, site: site)
        await sync.sync()
    }

    func doseRow(_ id: UUID) -> Row? { (try? rows.row("MedicationEntry", id.uuidString.lowercased())) ?? nil }
    func saveDose(id: UUID?, fields: Row) { upsert("MedicationEntry", id?.uuidString.lowercased(), fields) }
    func deleteDose(_ id: UUID) { write { try rows.delete("MedicationEntry", id.uuidString.lowercased()) } }

    func updateReminderSetting(_ id: UUID, _ fields: Row) { upsert("ReminderSetting", id.uuidString.lowercased(), fields) }

    // MARK: history

    func updateSession(_ id: UUID, _ fields: Row, sets: [(UUID, Row)]) {
        write {
            try rows.update("WorkoutSession", id.uuidString.lowercased(), fields)
            for (sid, f) in sets { try rows.update("ExerciseSet", sid.uuidString.lowercased(), f) }
        }
    }

    /// Deleting the day's last workout turns a Workout day back into Recovery (like the C# service).
    func deleteSession(_ id: UUID) {
        let day = sessions.first { $0.id == id }?.date
        write {
            try rows.delete("WorkoutSession", id.uuidString.lowercased())
            guard let day, !sessions.contains(where: { $0.id != id && $0.date == day }) else { return }
            let log = try rows.rows("DailyLog").first {
                if case .bool(true) = $0["isDeleted"] { return false }
                return $0.string("date").map { $0.hasPrefix(Self.dayString(day)) } ?? false
            }
            if let log, let logId = log.string("id"), log.int("dayType") == 1 { try rows.update("DailyLog", logId, ["dayType": .int(2)]) }
        }
    }

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
    private func rebuildEnergy() {
        energy = EnergyHistory(logs: health.overlay(logs), body: body, sessions: sessions)
        publishWidget()
    }

    /// Hand the widgets today's numbers (only reloading them when something actually changed).
    func publishWidget() {
        let today = Day.today
        let snap = WidgetSnapshot(updatedAt: Date(), day: WidgetSnapshot.today(), readiness: readiness?.score,
                                  level: readiness?.level, advice: readiness?.advice, caloriesIn: caloriesByDay[today] ?? 0,
                                  projectedOut: energy.out(today)?.kcal,
                                  avg7Net: energy.balance(today, 7, caloriesIn: caloriesByDay)?.avgNet,
                                  bars: widgetBars(), quick: quickItems())
        var old = WidgetSnapshot.load(); old?.updatedAt = snap.updatedAt
        guard old != snap else { return }
        snap.save()
        WidgetCenter.shared.reloadAllTimelines()
    }

    /// Today's goal bars, same targets as the Today screen.
    private func widgetBars() -> [WidgetSnapshot.Bar] {
        guard let g = goals else { return [] }
        let m = meals(on: .today), d = drinks(on: .today)
        let active = energy.log(.today)?.isActiveDay ?? false
        typealias B = WidgetSnapshot.Bar
        var bars = [
            B(label: "Calories", value: Double(Nutrition.calories(m, d)), low: Double(active ? g.caloriesActiveMin : g.caloriesRestMin),
              high: Double(active ? g.caloriesActiveMax : g.caloriesRestMax), ceiling: false, unit: "", key: "calories"),
            B(label: "Protein", value: Nutrition.protein(m, d), low: Double(g.proteinMin), high: Double(g.proteinMax), ceiling: false, unit: "g", key: "proteinG"),
            B(label: "Carbs", value: Nutrition.carbs(m, d), low: Double(active ? g.carbsActiveMin : g.carbsRestMin),
              high: Double(active ? g.carbsActiveMax : g.carbsRestMax), ceiling: false, unit: "g", key: "carbsG"),
            B(label: "Fat", value: Nutrition.fat(m, d), low: Double(g.fatMin), high: Double(g.fatMax), ceiling: false, unit: "g", key: "fatG"),
            B(label: "Fiber", value: Nutrition.fiber(m, d), low: Double(g.fiberMin), high: Double(g.fiberMax), ceiling: false, unit: "g", key: "fiberG"),
        ]
        if let max = g.addedSugarMax, max > 0 {
            bars.append(B(label: "Added sugar", value: Nutrition.addedSugar(m, d), low: 0, high: Double(max), ceiling: true, unit: "g", key: "addedSugarG"))
        }
        bars.append(B(label: "Sodium", value: Nutrition.sodium(m, d), low: 0, high: 2300, ceiling: true, unit: "mg", key: "sodiumMg"))
        if let lo = active ? g.waterActiveMin : g.waterRestMin, let hi = active ? g.waterActiveMax : g.waterRestMax, hi > 0 {
            bars.append(B(label: "Fluids", value: Double(Nutrition.fluidOz(d)), low: Double(lo), high: Double(hi), ceiling: false, unit: "oz", key: "ounces"))
        }
        return bars.filter { $0.high > 0 }
    }

    /// Water plus your three most-logged meals/drinks of the last 30 days, each as a full repeat
    /// (items included) ready to insert.
    private func quickItems() -> [WidgetSnapshot.QuickItem] {
        func macros(_ m: Macros, ounces: Double? = nil, sugarCount: Int? = nil) -> [String: Double] {
            var out: [String: Double] = [:]
            out["calories"] = m.calories.map(Double.init); out["proteinG"] = m.proteinG; out["carbsG"] = m.carbsG
            out["fatG"] = m.fatG; out["fiberG"] = m.fiberG; out["sodiumMg"] = m.sodiumMg.map(Double.init); out["ounces"] = ounces
            out["addedSugarG"] = (m.addedSugarG ?? 0) + Double(sugarCount ?? 0) * 4
            return out.compactMapValues { $0 }
        }
        let water: Row = ["description": .string("Water"), "ounces": .int(16)]
        var items = [WidgetSnapshot.QuickItem(id: "water", title: "Water", kind: "drink", icon: "drop.fill",
                                              row: (try? water.encoded()) ?? Data(), macros: ["ounces": 16])]
        let since = Date().addingTimeInterval(-30 * 86_400)
        var counts: [String: (n: Int, kind: String, name: String)] = [:]
        for m in meals where m.time >= since {
            let k = "meal:" + m.description.lowercased(); counts[k] = ((counts[k]?.n ?? 0) + 1, "meal", m.description)
        }
        for d in drinks where d.time >= since && d.description.caseInsensitiveCompare("Water") != .orderedSame {
            let k = "drink:" + d.description.lowercased(); counts[k] = ((counts[k]?.n ?? 0) + 1, "drink", d.description)
        }
        for (key, c) in counts.sorted(by: { $0.value.n > $1.value.n }).prefix(3) where c.n >= 2 {
            if c.kind == "meal" {
                var dr = MealDraft(); dr.description = c.name; dr.repeatLast(c.name, from: library)
                var row = dr.row; row.removeValue(forKey: "time"); row.removeValue(forKey: "mealType")
                items.append(.init(id: key, title: c.name, kind: "meal", icon: "fork.knife", row: (try? row.encoded()) ?? Data(), macros: macros(dr.macros)))
            } else {
                var dr = DrinkDraft(); dr.description = c.name; dr.repeatLast(c.name, from: library)
                var row = dr.row; row.removeValue(forKey: "time")
                items.append(.init(id: key, title: c.name, kind: "drink", icon: "cup.and.saucer.fill", row: (try? row.encoded()) ?? Data(),
                                   macros: macros(dr.macros, ounces: dr.ounces, sugarCount: dr.sugarCount)))
            }
        }
        return items
    }

    /// Insert what was logged from the widget (time = when you tapped), then sync.
    func drainQuickLogs() {
        var any = false
        WidgetSnapshot.drain { p in
            var row = try Row.decode(p.row)
            row["time"] = Wire.local(p.at)
            if p.kind == "meal" { row["mealType"] = .int(Int64(MealType.forHour(p.at).rawValue)) }
            _ = try rows.insert(p.kind == "meal" ? "MealEntry" : "DrinkEntry", row, now: p.at)
            any = true
        }
        if any { write { } }   // reload + push like any other edit
    }
}

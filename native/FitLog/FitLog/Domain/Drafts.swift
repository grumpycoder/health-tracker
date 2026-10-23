import Foundation

/// Editable copy of a meal. Loaded from the raw row and written back as only the fields it owns
/// (merged into the row by RowStore.update), so tags, AI fields and anything unmodeled are kept.
struct MealDraft {
    var id: String?
    var time = Date()
    var mealType: MealType = .snack
    var description = ""
    var portionNote = ""
    var satiety: Satiety = .unset
    var stars: Int?
    var macros = Macros()
    var items: [MealItem] = []

    init(type: MealType = MealType.forHour()) { mealType = type }

    init(id: String, row r: Row) {
        self.id = id
        time = r.date("time") ?? Date()
        mealType = MealType(rawValue: r.int("mealType") ?? 3) ?? .snack
        description = r.string("description") ?? ""
        portionNote = r.string("portionNote") ?? ""
        satiety = Satiety(rawValue: r.int("satiety") ?? 0) ?? .unset
        stars = r.int("qualityStars")
        macros = Macros(r)
        items = MealItem.parse(r.string("itemsJson"))
    }

    /// Recompute totals from items (only when there are items; otherwise totals are typed directly).
    mutating func setTotals() { if !items.isEmpty { macros = .of(items) } }

    /// Current totals as one item, so adding the first item doesn't drop them (C# BaseItem).
    var baseItem: MealItem? { macros.isEmpty ? nil : macros.item(named: description.isEmpty ? "Base" : description) }

    var row: Row {
        var r = macros.row
        r["time"] = Wire.local(time)
        r["mealType"] = .int(Int64(mealType.rawValue))
        r["description"] = .string(description.trimmingCharacters(in: .whitespaces))
        r["portionNote"] = .s(portionNote.isEmpty ? nil : portionNote)
        r["satiety"] = .int(Int64(satiety.rawValue))
        r["qualityStars"] = .i(stars)
        r["itemsJson"] = .s(MealItem.serialize(items))
        return r
    }
}

struct DrinkDraft {
    var id: String?
    var time = Date()
    var description = ""
    var ounces: Double?
    var sugarCount: Int?
    var macros = Macros()
    var items: [MealItem] = []

    init() {}

    init(id: String, row r: Row) {
        self.id = id
        time = r.date("time") ?? Date()
        description = r.string("description") ?? ""
        ounces = r.double("ounces")
        sugarCount = r.int("sugarCount")
        macros = Macros(r)
        items = MealItem.parse(r.string("itemsJson"))
    }

    mutating func setTotals() { if !items.isEmpty { macros = .of(items) } }
    var baseItem: MealItem? { macros.isEmpty ? nil : macros.item(named: description.isEmpty ? "Base" : description) }

    var row: Row {
        var r = macros.row
        r["time"] = Wire.local(time)
        r["description"] = .string(description.trimmingCharacters(in: .whitespaces))
        r["ounces"] = .d(ounces)
        r["sugarCount"] = .i(sugarCount)
        r["itemsJson"] = .s(MealItem.serialize(items))
        return r
    }
}

extension MealType {
    /// Sensible default by time of day.
    static func forHour(_ date: Date = Date()) -> MealType {
        switch Calendar.current.component(.hour, from: date) {
        case 4..<11: .breakfast
        case 11..<15: .lunch
        case 17..<22: .dinner
        default: .snack
        }
    }
}

/// Every food name the app knows, built like the C# FoodLibrary: saved foods win, then the newest
/// logged item breakdown, then whole meals by name. Macros are per serving.
struct FoodLibrary {
    private(set) var defaults: [String: MealItem] = [:]     // lowercased name → per-serving item
    private(set) var saved: [String: Row] = [:]             // lowercased name → SavedFoodEntry row
    private(set) var savedIds: [String: String] = [:]
    var names: [String] { defaults.values.map(\.name).sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending } }

    init() {}

    /// Built from already-decoded entries (cheap); only the few saved foods come as raw rows.
    init(meals: [MealEntry], drinks: [DrinkEntry], savedFoods: [(id: String, row: Row)]) {
        let ms = meals.sorted { $0.time > $1.time }
        let uses = (meals.map { ($0.time, $0.itemsJson) } + drinks.map { ($0.time, $0.itemsJson) })
            .filter { $0.1 != nil }.sorted { $0.0 > $1.0 }
        func put(_ uses: [MealItem]) {
            var seen = Set<String>()
            for u in uses {
                let n = u.name.trimmingCharacters(in: .whitespaces)
                guard !n.isEmpty, seen.insert(n.lowercased()).inserted else { continue }
                var c = u.copy(); c.name = n
                defaults[n.lowercased()] = c
            }
        }
        put(ms.map { $0.macros.item(named: $0.description) })
        put(uses.flatMap { MealItem.parse($0.1) })
        for (id, r) in savedFoods {
            guard let name = r.string("name")?.trimmingCharacters(in: .whitespaces), !name.isEmpty else { continue }
            saved[name.lowercased()] = r
            savedIds[name.lowercased()] = id
            defaults[name.lowercased()] = Macros(r).item(named: name)
        }
    }

    /// A fresh one-serving item for `name` (no macros when the name is new).
    func item(for name: String) -> MealItem {
        let n = name.trimmingCharacters(in: .whitespaces)
        return defaults[n.lowercased()]?.copy() ?? MealItem(name: n)
    }

    func suggest(_ query: String, max: Int = 8) -> [String] {
        let q = query.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty else { return [] }
        return names.filter { $0.localizedCaseInsensitiveContains(q) }
            .sorted { a, b in
                let pa = a.lowercased().hasPrefix(q.lowercased()), pb = b.lowercased().hasPrefix(q.lowercased())
                return pa != pb ? pa : a.count < b.count
            }
            .prefix(max).map { $0 }
    }
}

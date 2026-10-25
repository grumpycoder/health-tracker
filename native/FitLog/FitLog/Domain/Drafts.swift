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
    /// Tags carried from a repeated meal (new entries only; editing never touches tags).
    var carriedTags: String?

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
        r["description"] = .string(description.foodNameCased)
        r["portionNote"] = .s(portionNote.isEmpty ? nil : portionNote)
        r["satiety"] = .int(Int64(satiety.rawValue))
        r["qualityStars"] = .i(stars)
        r["itemsJson"] = .s(MealItem.serialize(items.map { var i = $0; i.name = i.name.foodNameCased; return i }))
        if id == nil, let t = carriedTags { r["tags"] = .string(t) }
        return r
    }

    /// Fill from the last time this meal was logged, like the MAUI app's repeat: portion note, tags,
    /// stars and macros; and its item breakdown at one serving each (saved-food macros win).
    mutating func repeatLast(_ name: String, from lib: FoodLibrary) {
        guard let m = lib.lastMeal[name.trimmingCharacters(in: .whitespaces).lowercased()] else {
            if macros.isEmpty { macros = Macros.of(lib.item(for: name)) }
            return
        }
        if portionNote.isEmpty { portionNote = m.portionNote ?? "" }
        carriedTags = m.tags
        stars = m.qualityStars
        items = lib.restore(m.items)
        macros = items.isEmpty ? m.macros : .of(items)
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
    var carriedTags: String?

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
        r["description"] = .string(description.foodNameCased)
        r["ounces"] = .d(ounces)
        r["sugarCount"] = .i(sugarCount)
        r["itemsJson"] = .s(MealItem.serialize(items.map { var i = $0; i.name = i.name.foodNameCased; return i }))
        if id == nil, let t = carriedTags { r["tags"] = .string(t) }
        return r
    }

    /// Repeat the last drink with this name: ounces, sugar, tags, macros and its add-ins.
    mutating func repeatLast(_ name: String, from lib: FoodLibrary) {
        guard let d = lib.lastDrink[name.trimmingCharacters(in: .whitespaces).lowercased()] else {
            if macros.isEmpty { macros = Macros.of(lib.item(for: name)) }
            return
        }
        if ounces == nil { ounces = d.ounces }
        if sugarCount == nil { sugarCount = d.sugarCount }
        carriedTags = d.tags
        items = lib.restore(d.items)
        macros = items.isEmpty ? d.macros : .of(items)
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
/// logged item breakdown, then whole meals and drinks by name. Macros are per serving.
struct FoodLibrary {
    private(set) var defaults: [String: MealItem] = [:]     // lowercased name → per-serving item
    private(set) var saved: [String: Row] = [:]             // lowercased name → SavedFoodEntry row
    private(set) var savedIds: [String: String] = [:]
    /// Newest entry per (lowercased) meal / drink name — for "repeat last time".
    private(set) var lastMeal: [String: MealEntry] = [:]
    private(set) var lastDrink: [String: DrinkEntry] = [:]
    /// Every spelling seen per lowercased name ("Coke zero" and "Coke Zero" share one entry above).
    private(set) var spellings: [String: Set<String>] = [:]
    /// Lowercased names hidden from suggestions (kept on this device, like the web and MAUI apps).
    var hidden: Set<String> = []
    /// Every known food, hidden ones included (the Saved foods screen).
    var allNames: [String] { defaults.values.map(\.name).sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending } }
    /// What suggestions offer.
    var names: [String] { allNames.filter { !hidden.contains($0.lowercased()) } }
    func isSaved(_ name: String) -> Bool { saved[name.trimmingCharacters(in: .whitespaces).lowercased()] != nil }

    init() {}

    /// Built from already-decoded entries (cheap); only the few saved foods come as raw rows.
    init(meals: [MealEntry], drinks: [DrinkEntry], savedFoods: [(id: String, row: Row)]) {
        let ms = meals.sorted { $0.time > $1.time }
        for m in ms { let k = m.description.trimmingCharacters(in: .whitespaces).lowercased(); if !k.isEmpty, lastMeal[k] == nil { lastMeal[k] = m } }
        for d in drinks.sorted(by: { $0.time > $1.time }) {
            let k = d.description.trimmingCharacters(in: .whitespaces).lowercased(); if !k.isEmpty, lastDrink[k] == nil { lastDrink[k] = d }
        }
        let uses = (meals.map { ($0.time, $0.itemsJson) } + drinks.map { ($0.time, $0.itemsJson) })
            .filter { $0.1 != nil }.sorted { $0.0 > $1.0 }
        func put(_ uses: [MealItem]) {
            var seen = Set<String>()
            for u in uses {
                let n = u.name.trimmingCharacters(in: .whitespaces)
                guard !n.isEmpty else { continue }
                spellings[n.lowercased(), default: []].insert(n)
                guard seen.insert(n.lowercased()).inserted else { continue }
                var c = u.copy(); c.name = n
                defaults[n.lowercased()] = c
            }
        }
        put(ms.map { $0.macros.item(named: $0.description) })
        // Drinks by name too (e.g. "Coke Zero", "Water"), newest first; a meal of the same name keeps its macros.
        put(drinks.sorted { $0.time > $1.time }.filter { lastMeal[$0.description.trimmingCharacters(in: .whitespaces).lowercased()] == nil }
                .map { $0.macros.item(named: $0.description) })
        put(uses.flatMap { MealItem.parse($0.1) })
        for (id, r) in savedFoods {
            guard let name = r.string("name")?.trimmingCharacters(in: .whitespaces), !name.isEmpty else { continue }
            spellings[name.lowercased(), default: []].insert(name)
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

    /// A stored breakdown at one serving each; items that are saved foods use the curated macros.
    func restore(_ items: [MealItem]) -> [MealItem] {
        items.map { saved[$0.name.trimmingCharacters(in: .whitespaces).lowercased()] != nil ? item(for: $0.name) : $0.copy() }
    }

    /// Typeahead: names containing the text, or whose words start with every typed word (so
    /// "eggs scr" finds "Scrambled eggs, 2"). A name that's the same food spelled differently
    /// comes first, then prefix matches, then shorter names.
    func suggest(_ query: String, max: Int = 8) -> [String] {
        let q = query.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty else { return [] }
        let qWords = Self.words(q), qKey = Self.key(q)
        return names.filter { n in
            n.localizedCaseInsensitiveContains(q) || {
                let w = Self.words(n)
                return !qWords.isEmpty && qWords.allSatisfy { t in w.contains { $0.hasPrefix(t) } }
            }()
        }
        .sorted { a, b in
            let ka = Self.key(a) == qKey, kb = Self.key(b) == qKey
            if ka != kb { return ka }
            let pa = a.lowercased().hasPrefix(q.lowercased()), pb = b.lowercased().hasPrefix(q.lowercased())
            return pa != pb ? pa : a.count < b.count
        }
        .prefix(max).map { $0 }
    }

    /// Lowercased words, punctuation dropped ("Eggs, Scrambled" → ["eggs", "scrambled"]).
    static func words(_ s: String) -> [String] {
        s.lowercased().components(separatedBy: CharacterSet.alphanumerics.inverted).filter { !$0.isEmpty }
    }
    /// Same key = same food written differently: capitalization, punctuation, spacing or word order.
    static func key(_ s: String) -> String { words(s).sorted().joined(separator: " ") }

    /// An existing name that's this one written differently (nil if none, or if it's spelled the same).
    func existingSpelling(of name: String) -> String? {
        let n = name.trimmingCharacters(in: .whitespaces), k = Self.key(n)
        guard !k.isEmpty else { return nil }
        return allNames.first { Self.key($0) == k && $0 != n }
    }

    /// Groups of spellings (from every log and saved food) that are the same food written
    /// differently — capitals, punctuation, spacing or word order — for merging.
    var duplicateGroups: [[String]] {
        let all = spellings.values.flatMap { $0 }.filter { !Self.key($0).isEmpty }
        return Dictionary(grouping: all, by: { Self.key($0) }).values
            .filter { $0.count > 1 }.map { $0.sorted() }
            .sorted { $0[0].localizedCaseInsensitiveCompare($1[0]) == .orderedAscending }
    }
}

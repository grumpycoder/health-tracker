import Foundation

/// .NET's Math.Round default (banker's rounding) — totals must match the C# apps exactly.
extension Double {
    var dotnetRounded: Double { rounded(.toNearestOrEven) }
    func dotnetRounded(_ places: Int) -> Double {
        let f = pow(10, Double(places))
        return (self * f).rounded(.toNearestOrEven) / f
    }
}

/// One item of a meal/drink breakdown — mirrors C# MealItemPortion. Macros are per serving;
/// `portion` is servings. Serialized into ItemsJson with .NET's default PascalCase names.
struct MealItem: Codable, Hashable, Identifiable {
    var id = UUID()   // UI identity only; not serialized
    var name: String = ""
    var calories: Int?
    var proteinG, carbsG, sugarG, addedSugarG, fatG: Double?
    var sodiumMg: Int?
    var fiberG: Double?
    var portion: Double = 1

    enum CodingKeys: String, CodingKey {
        case name = "Name", calories = "Calories", proteinG = "ProteinG", carbsG = "CarbsG", sugarG = "SugarG"
        case addedSugarG = "AddedSugarG", fatG = "FatG", sodiumMg = "SodiumMg", fiberG = "FiberG", portion = "Portion"
    }

    init(name: String = "", portion: Double = 1) { self.name = name; self.portion = portion }

    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        name = (try? c.decode(String.self, forKey: .name)) ?? ""
        calories = try? c.decode(Int.self, forKey: .calories)
        proteinG = try? c.decode(Double.self, forKey: .proteinG)
        carbsG = try? c.decode(Double.self, forKey: .carbsG)
        sugarG = try? c.decode(Double.self, forKey: .sugarG)
        addedSugarG = try? c.decode(Double.self, forKey: .addedSugarG)
        fatG = try? c.decode(Double.self, forKey: .fatG)
        sodiumMg = try? c.decode(Int.self, forKey: .sodiumMg)
        fiberG = try? c.decode(Double.self, forKey: .fiberG)
        portion = (try? c.decode(Double.self, forKey: .portion)) ?? 1
    }

    // Always write every key (null included), like System.Text.Json does.
    func encode(to e: Encoder) throws {
        var c = e.container(keyedBy: CodingKeys.self)
        try c.encode(name, forKey: .name)
        try c.encode(calories, forKey: .calories)
        try c.encode(proteinG, forKey: .proteinG)
        try c.encode(carbsG, forKey: .carbsG)
        try c.encode(sugarG, forKey: .sugarG)
        try c.encode(addedSugarG, forKey: .addedSugarG)
        try c.encode(fatG, forKey: .fatG)
        try c.encode(sodiumMg, forKey: .sodiumMg)
        try c.encode(fiberG, forKey: .fiberG)
        try c.encode(portion, forKey: .portion)
    }

    var kcal: Int { Int((Double(calories ?? 0) * portion).dotnetRounded) }

    static func parse(_ json: String?) -> [MealItem] {
        guard let json, let data = json.data(using: .utf8) else { return [] }
        return (try? JSONDecoder().decode([MealItem].self, from: data)) ?? []
    }

    /// nil when empty — matches C# MealItemPortion.Serialize.
    static func serialize(_ items: [MealItem]) -> String? {
        guard !items.isEmpty, let d = try? JSONEncoder().encode(items) else { return nil }
        return String(data: d, encoding: .utf8)
    }

    func copy(portion: Double = 1) -> MealItem { var c = self; c.id = UUID(); c.portion = portion; return c }
}

/// Macro totals shared by meals, drinks and items.
struct Macros: Equatable {
    var calories: Int?
    var proteinG, carbsG, sugarG, addedSugarG, fatG: Double?
    var sodiumMg: Int?
    var fiberG: Double?

    var isEmpty: Bool { self == Macros() }

    /// Sum of each item's per-serving macros × servings (C# web ItemTotals.Of); all nil when no items.
    static func of(_ items: [MealItem]) -> Macros {
        guard !items.isEmpty else { return Macros() }
        func sum(_ f: (MealItem) -> Double?) -> Double { items.reduce(0) { $0 + (f($1) ?? 0) * $1.portion } }
        return Macros(
            calories: Int(sum { $0.calories.map(Double.init) }.dotnetRounded),
            proteinG: sum(\.proteinG).dotnetRounded(1), carbsG: sum(\.carbsG).dotnetRounded(1),
            sugarG: sum(\.sugarG).dotnetRounded(1), addedSugarG: sum(\.addedSugarG).dotnetRounded(1),
            fatG: sum(\.fatG).dotnetRounded(1),
            sodiumMg: Int(sum { $0.sodiumMg.map(Double.init) }.dotnetRounded),
            fiberG: sum(\.fiberG).dotnetRounded(1))
    }

    init(calories: Int? = nil, proteinG: Double? = nil, carbsG: Double? = nil, sugarG: Double? = nil,
         addedSugarG: Double? = nil, fatG: Double? = nil, sodiumMg: Int? = nil, fiberG: Double? = nil) {
        self.calories = calories; self.proteinG = proteinG; self.carbsG = carbsG; self.sugarG = sugarG
        self.addedSugarG = addedSugarG; self.fatG = fatG; self.sodiumMg = sodiumMg; self.fiberG = fiberG
    }

    init(_ r: Row) {
        self.init(calories: r.int("calories"), proteinG: r.double("proteinG"), carbsG: r.double("carbsG"),
                  sugarG: r.double("sugarG"), addedSugarG: r.double("addedSugarG"), fatG: r.double("fatG"),
                  sodiumMg: r.int("sodiumMg"), fiberG: r.double("fiberG"))
    }

    var row: Row {
        ["calories": .i(calories), "proteinG": .d(proteinG), "carbsG": .d(carbsG), "sugarG": .d(sugarG),
         "addedSugarG": .d(addedSugarG), "fatG": .d(fatG), "sodiumMg": .i(sodiumMg), "fiberG": .d(fiberG)]
    }

    func item(named name: String, portion: Double = 1) -> MealItem {
        var it = MealItem(name: name, portion: portion)
        it.calories = calories; it.proteinG = proteinG; it.carbsG = carbsG; it.sugarG = sugarG
        it.addedSugarG = addedSugarG; it.fatG = fatG; it.sodiumMg = sodiumMg; it.fiberG = fiberG
        return it
    }

    static func of(_ it: MealItem) -> Macros {
        Macros(calories: it.calories, proteinG: it.proteinG, carbsG: it.carbsG, sugarG: it.sugarG,
               addedSugarG: it.addedSugarG, fatG: it.fatG, sodiumMg: it.sodiumMg, fiberG: it.fiberG)
    }
}

enum Satiety: Int, CaseIterable, Identifiable {
    case unset, stillHungry, satisfied, full, bloated, emptyStomach
    var id: Int { rawValue }
    var label: String { ["Not rated", "Still hungry", "Satisfied", "Full", "Bloated", "Empty stomach"][rawValue] }
}

// MARK: - Row field access / optional JSON builders

extension Row {
    func string(_ k: String) -> String? { if case .string(let s) = self[k] { s } else { nil } }
    func int(_ k: String) -> Int? {
        switch self[k] { case .int(let i): Int(i); case .double(let d): Int(d); default: nil }
    }
    func double(_ k: String) -> Double? {
        switch self[k] { case .int(let i): Double(i); case .double(let d): d; default: nil }
    }
    func date(_ k: String) -> Date? { string(k).flatMap(Wire.parse) }
}

/// Optional → JSON (nil becomes null). Named apart from the enum cases to avoid overload ambiguity.
extension JSON {
    static func i(_ v: Int?) -> JSON { v.map { .int(Int64($0)) } ?? .null }
    static func d(_ v: Double?) -> JSON { v.map { .double($0) } ?? .null }
    static func s(_ v: String?) -> JSON { v.map { .string($0) } ?? .null }
}

/// Renaming a food everywhere it's used, like the web Saved Foods rename: a meal's or drink's
/// name and any item in its breakdown, matched trimmed and case-insensitively (so variants such
/// as "Coke zero" / "Coke Zero" merge). Works on the raw row so nothing else is touched.
enum FoodRename {
    static func matches(_ name: String?, _ old: String) -> Bool {
        name?.trimmingCharacters(in: .whitespaces).caseInsensitiveCompare(old.trimmingCharacters(in: .whitespaces)) == .orderedSame
    }

    /// The fields to change on a MealEntry/DrinkEntry row, or nil when it doesn't use `old`.
    static func changes(_ row: Row, from old: String, to new: String) -> Row? {
        var out: Row = [:]
        if matches(row.string("description"), old) { out["description"] = .string(new) }
        if let json = row.string("itemsJson"), let data = json.data(using: .utf8),
           case .array(let items)? = try? JSONDecoder().decode(JSON.self, from: data) {
            var hit = false
            let renamed: [JSON] = items.map { it in
                guard case .object(var o) = it, case .string(let n)? = o["Name"], matches(n, old) else { return it }
                hit = true; o["Name"] = .string(new); return .object(o)
            }
            if hit, let d = try? JSONEncoder().encode(JSON.array(renamed)), let s = String(data: d, encoding: .utf8) {
                out["itemsJson"] = .string(s)
            }
        }
        return out.isEmpty ? nil : out
    }
}

extension String {
    /// Food names in title case: "protein shake with whole milk" → "Protein Shake with Whole Milk".
    /// Only all-lowercase words change, so "BBQ", "McDonald's" or "iced LATTE" keep their capitals;
    /// words with digits ("2%", "16oz") and units (oz, g, tbsp…) stay as typed, and small joining
    /// words stay lowercase unless they start the name. Hyphenated parts are cased separately.
    var foodNameCased: String {
        let small: Set<String> = ["a", "an", "and", "or", "of", "with", "w/", "in", "on", "the", "to", "for", "&"]
        let units: Set<String> = ["oz", "fl", "g", "mg", "kg", "ml", "l", "lb", "lbs", "tsp", "tbsp", "cal", "kcal"]
        func cap(_ w: Substring) -> String {
            guard let f = w.first, w == w.lowercased(), !w.contains(where: \.isNumber) else { return String(w) }
            return f.uppercased() + w.dropFirst()
        }
        var first = true
        return trimmingCharacters(in: .whitespaces).split(separator: " ", omittingEmptySubsequences: false).map { word -> String in
            defer { if !word.isEmpty { first = false } }
            let bare = word.lowercased().trimmingCharacters(in: .punctuationCharacters)
            if word.isEmpty || units.contains(bare) || (!first && small.contains(word.lowercased())) { return String(word) }
            return word.split(separator: "-", omittingEmptySubsequences: false).map(cap).joined(separator: "-")
        }.joined(separator: " ")
    }
}

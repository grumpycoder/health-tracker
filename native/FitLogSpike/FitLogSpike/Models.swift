import Foundation

// Read-only mirrors of the synced C# entities (FitRecoveryLog.Shared), decoding the server's
// camelCase JSON. Only the fields this trial needs; unknown fields are ignored.

struct MealItem: Decodable, Hashable {
    var name: String
    var calories: Int?
    var proteinG, carbsG, fatG: Double?
    var portion: Double
    enum CodingKeys: String, CodingKey { case name = "Name", calories = "Calories", proteinG = "ProteinG", carbsG = "CarbsG", fatG = "FatG", portion = "Portion" }
    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        name = (try? c.decode(String.self, forKey: .name)) ?? ""
        calories = try? c.decode(Int.self, forKey: .calories)
        proteinG = try? c.decode(Double.self, forKey: .proteinG)
        carbsG = try? c.decode(Double.self, forKey: .carbsG)
        fatG = try? c.decode(Double.self, forKey: .fatG)
        portion = (try? c.decode(Double.self, forKey: .portion)) ?? 1
    }
    var kcal: Int { Int(((Double(calories ?? 0)) * portion).rounded()) }

    /// ItemsJson is serialized by .NET with default (PascalCase) names.
    static func parse(_ json: String?) -> [MealItem] {
        guard let json, let data = json.data(using: .utf8) else { return [] }
        return (try? JSONDecoder().decode([MealItem].self, from: data)) ?? []
    }
}

enum MealType: Int, Decodable, CaseIterable {
    case breakfast, lunch, dinner, snack, drink
    var label: String { ["Breakfast", "Lunch", "Dinner", "Snack", "Drink"][rawValue] }
    var symbol: String { ["sunrise", "sun.max", "moon.stars", "carrot", "cup.and.saucer"][rawValue] }
}

struct MealEntry: Decodable, Identifiable {
    let id: UUID
    let isDeleted: Bool
    let time: Date
    let mealType: MealType
    let description: String
    let portionNote: String?
    let satiety: Int
    let qualityStars: Int?
    let calories: Int?
    let proteinG, carbsG, sugarG, fatG, fiberG, addedSugarG: Double?
    let sodiumMg: Int?
    let itemsJson: String?
    var items: [MealItem] { MealItem.parse(itemsJson) }
}

struct DrinkEntry: Decodable, Identifiable {
    let id: UUID
    let isDeleted: Bool
    let time: Date
    let description: String
    let ounces: Double?
    let sugarCount: Int?
    let calories: Int?
    let proteinG, carbsG, sugarG, fatG, fiberG, addedSugarG: Double?
    let sodiumMg: Int?
    let itemsJson: String?
    var items: [MealItem] { MealItem.parse(itemsJson) }
}

struct DailyLog: Decodable {
    let isDeleted: Bool
    let date: Day
    let dayType: Int
    let updatedAt: Date
    let restingEnergyKcal: Int?
    let activeEnergyKcal: Int?
    var isActiveDay: Bool { dayType == 1 || dayType == 4 }   // Workout / HighWorkload
}

struct BodyMeasurement: Decodable {
    let isDeleted: Bool
    let date: Day
    let weightLbs: Double?
    let basalMetabolicRate: Int?
}

struct WorkoutSession: Decodable {
    let isDeleted: Bool
    let date: Day
    let totalSeconds: Int?
}

struct GoalSettings: Decodable {
    let isDeleted: Bool
    let updatedAt: Date
    let proteinMin, proteinMax, fatMin, fatMax, fiberMin, fiberMax: Int
    let caloriesRestMin, caloriesRestMax, caloriesActiveMin, caloriesActiveMax: Int
    let carbsRestMin, carbsRestMax, carbsActiveMin, carbsActiveMax: Int
}

/// A calendar day (C# DateOnly, "yyyy-MM-dd") — no time zone games.
struct Day: Hashable, Comparable, Decodable {
    let y, m, d: Int
    init(y: Int, m: Int, d: Int) { self.y = y; self.m = m; self.d = d }
    init(_ date: Date) {
        let c = Calendar.current.dateComponents([.year, .month, .day], from: date)
        self.init(y: c.year!, m: c.month!, d: c.day!)
    }
    init(from decoder: Decoder) throws {
        let s = try decoder.singleValueContainer().decode(String.self)
        let p = s.prefix(10).split(separator: "-").compactMap { Int($0) }
        guard p.count == 3 else { throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath, debugDescription: s)) }
        self.init(y: p[0], m: p[1], d: p[2])
    }
    static var today: Day { Day(Date()) }
    var date: Date { Calendar.current.date(from: DateComponents(year: y, month: m, day: d))! }
    func adding(_ days: Int) -> Day { Day(Calendar.current.date(byAdding: .day, value: days, to: date)!) }
    static func < (a: Day, b: Day) -> Bool { (a.y, a.m, a.d) < (b.y, b.m, b.d) }
}

enum WireDate {
    /// .NET writes DateTime as "2026-10-21T09:00:00", optionally with 1–7 fractional digits
    /// and a "Z" (UpdatedAt is UTC). Local times carry no zone and are taken as device-local.
    static func parse(_ s: String) -> Date? {
        var str = s
        var utc = false
        if str.hasSuffix("Z") { utc = true; str.removeLast() }
        if let dot = str.firstIndex(of: ".") {
            let frac = str[str.index(after: dot)...].prefix(3)
            str = String(str[..<dot]) + "." + frac.padding(toLength: 3, withPad: "0", startingAt: 0)
        } else { str += ".000" }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSS"
        f.timeZone = utc ? TimeZone(identifier: "UTC") : .current
        return f.date(from: str)
    }

    static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .custom { dec in
            let s = try dec.singleValueContainer().decode(String.self)
            guard let date = parse(s) else {
                throw DecodingError.dataCorrupted(.init(codingPath: dec.codingPath, debugDescription: "Bad date \(s)"))
            }
            return date
        }
        return d
    }()
}

import Foundation

// Typed read views of the synced C# entities (FitRecoveryLog.Shared), decoded from the raw rows in
// RowStore (server camelCase JSON). Only modeled fields are read; the raw row keeps the rest for push.
// Deleted rows are filtered by RowStore.decode, so models don't carry isDeleted.

enum MealType: Int, Decodable, CaseIterable {
    case breakfast, lunch, dinner, snack, drink
    var label: String { ["Breakfast", "Lunch", "Dinner", "Snack", "Drink"][rawValue] }
    var symbol: String { ["sunrise", "sun.max", "moon.stars", "carrot", "cup.and.saucer"][rawValue] }
}

struct MealEntry: Decodable, Identifiable {
    let id: UUID
    let time: Date
    let mealType: MealType
    let description: String
    let portionNote: String?
    let tags: String?
    let satiety: Int
    let qualityStars: Int?
    let calories: Int?
    let proteinG, carbsG, sugarG, fatG, fiberG, addedSugarG: Double?
    let sodiumMg: Int?
    let itemsJson: String?
    var items: [MealItem] { MealItem.parse(itemsJson) }
    var macros: Macros { Macros(calories: calories, proteinG: proteinG, carbsG: carbsG, sugarG: sugarG, addedSugarG: addedSugarG,
                                fatG: fatG, sodiumMg: sodiumMg, fiberG: fiberG) }
}

struct DrinkEntry: Decodable, Identifiable {
    let id: UUID
    let time: Date
    let description: String
    let ounces: Double?
    let sugarCount: Int?
    let tags: String?
    let calories: Int?
    let proteinG, carbsG, sugarG, fatG, fiberG, addedSugarG: Double?
    let sodiumMg: Int?
    let itemsJson: String?
    var items: [MealItem] { MealItem.parse(itemsJson) }
    var macros: Macros { Macros(calories: calories, proteinG: proteinG, carbsG: carbsG, sugarG: sugarG, addedSugarG: addedSugarG,
                                fatG: fatG, sodiumMg: sodiumMg, fiberG: fiberG) }
}

// Energy inputs: everything but the date is optional so the shared test vectors (which only
// carry the fields that matter) decode into the same types.
struct DailyLog: Decodable {
    var date: Day
    var dayType: Int?
    var updatedAt: Date?
    var restingEnergyKcal: Int?
    var activeEnergyKcal: Int?
    var isActiveDay: Bool { dayType == 1 || dayType == 4 }   // Workout / HighWorkload
}

struct BodyMeasurement: Decodable, Identifiable {
    var id: UUID?            // optional so the shared energy test vectors decode
    var date: Day
    var weightLbs: Double?
    var waistInches, chestInches, hipsInches, armsInches, forearmsInches, thighsInches, shouldersInches, calvesInches: Double?
    var bodyFatPercent, muscleMassLbs, visceralFat, bodyWaterPercent: Double?
    var basalMetabolicRate: Int?
    var metabolicAge: Int?
    var clothingFitNotes: String?
}

struct WorkoutSession: Decodable, Identifiable {
    var id: UUID?            // optional so the shared energy test vectors decode
    var date: Day
    var routineId: UUID?
    var startedAt: Date?
    var endedAt: Date?
    var totalSeconds: Int?
    var notes: String?
}

struct GoalSettings: Decodable {
    let updatedAt: Date
    var coachingGoals: String?
    let proteinMin, proteinMax, fatMin, fatMax, fiberMin, fiberMax: Int
    let caloriesRestMin, caloriesRestMax, caloriesActiveMin, caloriesActiveMax: Int
    let carbsRestMin, carbsRestMax, carbsActiveMin, carbsActiveMax: Int
    let addedSugarMax: Int?
    let waterRestMin, waterRestMax, waterActiveMin, waterActiveMax: Int?
    let goalWeightLbs, goalWaistInches, goalBodyFatPercent, goalChestInches: Double?
    let goalShouldersInches, goalArmsInches, goalThighsInches, goalCalvesInches: Double?
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

/// The calendar day `date` falls on in `tz` (a sample's recorded time zone, so travel doesn't re-key nights).
extension Day {
    init(_ date: Date, in tz: TimeZone) {
        var cal = Calendar(identifier: .gregorian); cal.timeZone = tz
        let c = cal.dateComponents([.year, .month, .day], from: date)
        self.init(y: c.year!, m: c.month!, d: c.day!)
    }
    var iso: String { String(format: "%04d-%02d-%02d", y, m, d) }
    init?(iso: String) {
        let p = iso.prefix(10).split(separator: "-").compactMap { Int($0) }
        guard p.count == 3 else { return nil }
        self.init(y: p[0], m: p[1], d: p[2])
    }
}

/// Encoded as "yyyy-MM-dd", and as a plain object key in dictionaries (readiness snapshots).
extension Day: Encodable, CodingKeyRepresentable {
    func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(iso) }
    private struct Key: CodingKey {
        let stringValue: String
        init(stringValue: String) { self.stringValue = stringValue }
        var intValue: Int? { nil }
        init?(intValue: Int) { nil }
    }
    var codingKey: CodingKey { Key(stringValue: iso) }
    init?<T: CodingKey>(codingKey: T) {
        let p = codingKey.stringValue.split(separator: "-").compactMap { Int($0) }
        guard p.count == 3 else { return nil }
        self.init(y: p[0], m: p[1], d: p[2])
    }
}

struct SleepEntry: Decodable, Identifiable {
    let id: UUID
    let date: Day
    let durationHours: Double?
    let sleepScore: Int?
    let interruptions: Int?
    let notes: String?
    let scoreEstimated: Bool?
}

struct RecoveryEntry: Decodable, Identifiable {
    let id: UUID
    let date: Day
    let recoveryRating, fatigueRating: Int?
    let sorenessLocations: String?
    let sorenessSeverity: Int?
    let notes: String?
    /// Written by the readiness auto-fill (vs a check-in you made yourself).
    var isAuto: Bool { notes?.hasPrefix(RecoveryEntry.autoMarker) ?? false }
    static let autoMarker = "Auto · readiness"
}

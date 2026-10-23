import Foundation

/// What the widgets show, written by the app into the shared App Group whenever its numbers change
/// (the widget extension can't read the app's database). Compiled into both targets.
struct WidgetSnapshot: Codable, Equatable {
    static let appGroup = "group.com.mlawrence.fitlog"

    var updatedAt: Date
    var day: String                 // yyyy-MM-dd the numbers are for
    var readiness: Int?
    var level: String?              // Ready / Moderate / Low / Rest
    var advice: String?
    var caloriesIn: Int
    var projectedOut: Int?
    var avg7Net: Int?               // 7-day average deficit (−) / surplus (+)

    /// Calories left before matching the projected burn (negative = over).
    var budget: Int? { projectedOut.map { $0 - caloriesIn } }

    private static var url: URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup)?.appendingPathComponent("widget.json")
    }
    static func load() -> WidgetSnapshot? { url.flatMap { try? Data(contentsOf: $0) }.flatMap { try? JSONDecoder().decode(Self.self, from: $0) } }
    func save() { if let u = Self.url, let d = try? JSONEncoder().encode(self) { try? d.write(to: u, options: .atomic) } }

    static func today() -> String {
        let c = Calendar.current.dateComponents([.year, .month, .day], from: Date())
        return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!)
    }
    var isToday: Bool { day == Self.today() }
}

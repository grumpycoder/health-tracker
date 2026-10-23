import Foundation

/// What the widgets show, written by the app into the shared App Group whenever its numbers change
/// (the widget extension can't read the app's database). Compiled into both targets.
struct WidgetSnapshot: Codable, Equatable {
    static let appGroup = "group.com.mlawrence.fitlog"

    /// One goal bar (range, or a ceiling like sodium).
    struct Bar: Codable, Equatable, Identifiable {
        var id: String { label }
        let label: String
        var value: Double
        let low: Double
        let high: Double
        let ceiling: Bool
        let unit: String
        /// Which QuickItem macro feeds this bar when a quick log is tapped.
        let key: String
    }

    /// A one-tap log: the full row the app will insert (items included), plus its macros so the
    /// widget can update its numbers immediately.
    struct QuickItem: Codable, Equatable, Identifiable {
        let id: String
        let title: String
        let kind: String            // "meal" or "drink"
        let icon: String
        let row: Data               // JSON fields for the RowStore insert (time/mealType set at insert)
        let macros: [String: Double]   // calories, proteinG, carbsG, fatG, fiberG, addedSugarG, sodiumMg, ounces
    }

    var updatedAt: Date
    var day: String                 // yyyy-MM-dd the numbers are for
    var readiness: Int?
    var level: String?              // Ready / Moderate / Low / Rest
    var advice: String?
    var caloriesIn: Int
    var projectedOut: Int?
    var avg7Net: Int?               // 7-day average deficit (−) / surplus (+)
    var bars: [Bar] = []
    var quick: [QuickItem] = []

    /// Calories left before matching the projected burn (negative = over).
    var budget: Int? { projectedOut.map { $0 - caloriesIn } }

    private static var dir: URL? { FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup) }
    private static var url: URL? { dir?.appendingPathComponent("widget.json") }
    static func load() -> WidgetSnapshot? { url.flatMap { try? Data(contentsOf: $0) }.flatMap { try? JSONDecoder().decode(Self.self, from: $0) } }
    func save() { if let u = Self.url, let d = try? JSONEncoder().encode(self) { try? d.write(to: u, options: .atomic) } }

    static func today() -> String {
        let c = Calendar.current.dateComponents([.year, .month, .day], from: Date())
        return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!)
    }
    var isToday: Bool { day == Self.today() }

    /// Apply a quick log to the displayed numbers (the app recomputes exactly on its next run).
    mutating func apply(_ item: QuickItem) {
        caloriesIn += Int(item.macros["calories"] ?? 0)
        for i in bars.indices { bars[i].value += item.macros[bars[i].key] ?? 0 }
        updatedAt = Date()
    }

    // MARK: quick-log hand-off — one file per tap, so the widget and app never race on a shared file

    struct PendingLog: Codable {
        let kind: String
        let row: Data
        let at: Date
    }

    static var queueDir: URL? {
        guard let d = dir?.appendingPathComponent("quicklog", isDirectory: true) else { return nil }
        try? FileManager.default.createDirectory(at: d, withIntermediateDirectories: true)
        return d
    }

    static func enqueue(_ p: PendingLog) {
        guard let d = queueDir, let data = try? JSONEncoder().encode(p) else { return }
        try? data.write(to: d.appendingPathComponent("\(UUID().uuidString).json"), options: .atomic)
    }

    /// Hand each pending log to `handle`; files are removed only after it succeeds.
    static func drain(_ handle: (PendingLog) throws -> Void) {
        guard let d = queueDir, let files = try? FileManager.default.contentsOfDirectory(at: d, includingPropertiesForKeys: nil) else { return }
        for f in files.sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) where f.pathExtension == "json" {
            guard let data = try? Data(contentsOf: f), let p = try? JSONDecoder().decode(PendingLog.self, from: data) else {
                try? FileManager.default.removeItem(at: f); continue
            }
            do { try handle(p); try? FileManager.default.removeItem(at: f) } catch { }
        }
    }
}

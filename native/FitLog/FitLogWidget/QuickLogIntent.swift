import AppIntents
import WidgetKit

/// A widget button tap: queue the entry for the app to insert, and update the widget's numbers now.
struct QuickLogIntent: AppIntent {
    static var title: LocalizedStringResource = "Quick log"
    static var description = IntentDescription("Log a favorite meal or drink from the widget.")

    @Parameter(title: "Item") var itemID: String

    init() {}
    init(itemID: String) { self.itemID = itemID }

    func perform() async throws -> some IntentResult {
        guard var snap = WidgetSnapshot.load(), let item = snap.quick.first(where: { $0.id == itemID }) else { return .result() }
        WidgetSnapshot.enqueue(.init(kind: item.kind, row: item.row, at: Date()))
        snap.apply(item)
        snap.save()
        return .result()   // WidgetKit reloads the widget after an intent runs
    }
}

import SwiftUI

/// Every food the app suggests: edit its per-serving macros (saved for next time), reset to the
/// last log, or hide it from suggestions. Past logs are never changed.
struct SavedFoodsView: View {
    @EnvironmentObject private var store: Store
    @State private var filter = ""
    @State private var editing: MealItem?

    private var foods: [String] {
        let q = filter.trimmingCharacters(in: .whitespaces)
        return store.library.allNames.filter { q.isEmpty || $0.localizedCaseInsensitiveContains(q) }
    }

    var body: some View {
        List {
            if foods.isEmpty { Text(filter.isEmpty ? "No foods logged yet." : "No matches.").foregroundStyle(.secondary) }
            ForEach(foods, id: \.self) { name in
                let hidden = store.hiddenFoods.contains(name.lowercased())
                Button { editing = store.library.item(for: name) } label: { row(name, hidden: hidden) }
                    .swipeActions(edge: .trailing) {
                        Button(hidden ? "Show" : "Hide") { store.toggleHidden(name) }.tint(hidden ? .blue : .gray)
                        if store.library.isSaved(name) {
                            Button("Reset", role: .destructive) { store.resetFood(name) }
                        }
                    }
            }
        }
        .searchable(text: $filter, prompt: "Filter foods")
        .navigationTitle("Saved foods")
        .sheet(item: $editing) { FoodMacrosSheet(item: $0) }
        .safeAreaInset(edge: .bottom) {
            Text("Tap to edit macros · swipe to hide from suggestions or reset to the last log")
                .font(.caption).foregroundStyle(.secondary).padding(8).frame(maxWidth: .infinity).background(.bar)
        }
    }

    private func row(_ name: String, hidden: Bool) -> some View {
        let it = store.library.item(for: name)
        let saved = store.library.isSaved(name)
        let macros = [it.calories.map { "\($0) cal" }, it.proteinG.map { "P \(GoalProgress.f1($0))g" },
                      it.carbsG.map { "C \(GoalProgress.f1($0))g" }, it.fatG.map { "F \(GoalProgress.f1($0))g" }].compactMap { $0 }
        return VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 6) {
                Text(name).foregroundStyle(hidden ? .secondary : .primary)
                if saved { Text("saved").font(.caption2.weight(.medium)).foregroundStyle(.tint) }
                if hidden { Image(systemName: "eye.slash").font(.caption).foregroundStyle(.secondary).accessibilityLabel("hidden") }
            }
            if !macros.isEmpty {
                Text(macros.joined(separator: " · ") + (saved ? "" : " (from last log)")).font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

/// Per-serving macros for one food; Save stores them as the food's saved macros.
private struct FoodMacrosSheet: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let item: MealItem
    @State private var macros = Macros()

    var body: some View {
        NavigationStack {
            Form {
                Section { MacroFields(macros: $macros) } header: { Text("Per serving") } footer: {
                    Text("Used next time you log this food or add it to a meal. Past logs aren't changed.")
                }
                if store.library.isSaved(item.name) {
                    Section {
                        Button("Reset to last log", role: .destructive) { store.resetFood(item.name); dismiss() }
                    }
                }
                Section {
                    let hidden = store.hiddenFoods.contains(item.name.lowercased())
                    Button(hidden ? "Show in suggestions" : "Hide from suggestions") { store.toggleHidden(item.name); dismiss() }
                }
            }
            .navigationTitle(item.name)
            .navigationBarTitleDisplayMode(.inline)
            .onAppear { macros = .of([item]) }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") { store.saveFood(macros.item(named: item.name)); dismiss() }
                }
            }
        }
    }
}

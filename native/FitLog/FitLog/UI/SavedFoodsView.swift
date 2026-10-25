import SwiftUI

/// Every food the app suggests: edit its per-serving macros (saved for next time), reset to the
/// last log, or hide it from suggestions. Past logs are never changed.
struct SavedFoodsView: View {
    @EnvironmentObject private var store: Store
    @State private var filter = ""
    @State private var editing: MealItem?
    @State private var showHidden = false

    private var foods: [String] {
        let q = filter.trimmingCharacters(in: .whitespaces)
        return store.library.allNames.filter { q.isEmpty || $0.localizedCaseInsensitiveContains(q) }
    }

    var body: some View {
        let shown = foods.filter { !store.hiddenFoods.contains($0.lowercased()) }
        let hidden = foods.filter { store.hiddenFoods.contains($0.lowercased()) }
        List {
            Section {
                if shown.isEmpty { Text(filter.isEmpty ? "No foods logged yet." : "No matches.").foregroundStyle(.secondary) }
                ForEach(shown, id: \.self) { foodRow($0, hidden: false) }
            } header: { Text("In suggestions") }
            if !hidden.isEmpty {
                Section {
                    if showHidden || !filter.isEmpty { ForEach(hidden, id: \.self) { foodRow($0, hidden: true) } }
                } header: {
                    Button { withAnimation { showHidden.toggle() } } label: {
                        HStack {
                            Text("Hidden (\(hidden.count))")
                            Spacer()
                            Image(systemName: "chevron.right").rotationEffect(.degrees(showHidden || !filter.isEmpty ? 90 : 0))
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
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

    private func foodRow(_ name: String, hidden: Bool) -> some View {
        Button { editing = store.library.item(for: name) } label: { row(name, hidden: hidden) }
            .swipeActions(edge: .trailing) {
                Button(hidden ? "Show" : "Hide") { store.toggleHidden(name) }.tint(hidden ? .blue : .gray)
                if store.library.isSaved(name) {
                    Button("Reset", role: .destructive) { store.resetFood(name) }
                }
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

/// One food's name and per-serving macros. Saving macros stores them as the food's saved macros;
/// changing the name renames it in every log that uses it (after a confirmation).
private struct FoodMacrosSheet: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let item: MealItem
    @State private var macros = Macros()
    @State private var name = ""
    @State private var confirmRename = false
    @State private var uses = 0

    private var newName: String { name.trimmingCharacters(in: .whitespaces) }
    private var renaming: Bool { !newName.isEmpty && newName != item.name }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name", text: $name).autocorrectionDisabled()
                } header: { Text("Name") } footer: {
                    if renaming {
                        let merge = store.library.allNames.contains { $0.caseInsensitiveCompare(newName) == .orderedSame && $0.caseInsensitiveCompare(item.name) != .orderedSame }
                        Text(merge ? "“\(newName)” already exists — the two will be merged into one food." : "Renames it in every meal and drink that uses it.")
                    }
                }
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
            .onAppear { macros = .of(item); name = item.name }
            .confirmationDialog(renameTitle, isPresented: $confirmRename, titleVisibility: .visible) {
                Button("Rename") {
                    store.renameFood(item.name, to: newName, macros: macros == .of(item) ? nil : macros)
                    dismiss()
                }
            } message: { Text("Past logs are updated to the new name. This can't be undone.") }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        if renaming { uses = store.foodUseCount(item.name); confirmRename = true } else { store.saveFood(macros.item(named: item.name)); dismiss() }
                    }
                    .disabled(newName.isEmpty)
                }
            }
        }
    }

    private var renameTitle: String {
        "Rename “\(item.name)” to “\(newName)”? This changes \(uses) log\(uses == 1 ? "" : "s")."
    }
}

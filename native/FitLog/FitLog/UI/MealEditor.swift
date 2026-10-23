import SwiftUI

/// Add or edit a meal. With items, the totals are their sum; without, totals are typed directly.
struct MealEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    @State var draft: MealDraft
    @State private var confirmDelete = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    FoodNameField(title: "What did you eat?", text: $draft.description) { name in
                        // Picking a known food on a new, empty meal fills in its last breakdown/macros.
                        guard draft.id == nil, draft.items.isEmpty, draft.macros.isEmpty else { return }
                        let it = store.library.item(for: name)
                        draft.macros = Macros.of(it)
                    }
                    Picker("Meal", selection: $draft.mealType) {
                        ForEach(MealType.allCases.filter { $0 != .drink }, id: \.self) { Label($0.label, systemImage: $0.symbol).tag($0) }
                    }
                    DatePicker("Time", selection: $draft.time)
                    TextField("Portion note (optional)", text: $draft.portionNote)
                }
                ItemsSection(items: $draft.items, base: { draft.baseItem }, onChange: { draft.setTotals() })
                MacrosSection(macros: $draft.macros, locked: !draft.items.isEmpty)
                Section("How full?") {
                    Picker("Fullness", selection: $draft.satiety) {
                        ForEach(Satiety.allCases) { Text($0.label).tag($0) }
                    }
                    Stepper(value: Binding(get: { draft.stars ?? 0 }, set: { draft.stars = $0 == 0 ? nil : $0 }), in: 0...5) {
                        LabeledContent("Goal fit", value: draft.stars.map { String(repeating: "★", count: $0) } ?? "—")
                    }
                }
                if draft.id != nil {
                    Section { Button("Delete meal", role: .destructive) { confirmDelete = true } }
                }
            }
            .navigationTitle(draft.id == nil ? "Add meal" : "Edit meal")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") { store.save(draft); dismiss() }
                        .disabled(draft.description.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
            .confirmationDialog("Delete this meal?", isPresented: $confirmDelete, titleVisibility: .visible) {
                Button("Delete", role: .destructive) {
                    if let id = draft.id.flatMap(UUID.init(uuidString:)) { store.deleteMeal(id) }
                    dismiss()
                }
            }
        }
    }
}

struct DrinkEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    @State var draft: DrinkDraft
    @State private var confirmDelete = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    FoodNameField(title: "Drink", text: $draft.description) { name in
                        guard draft.id == nil, draft.items.isEmpty, draft.macros.isEmpty else { return }
                        draft.macros = Macros.of(store.library.item(for: name))
                    }
                    DatePicker("Time", selection: $draft.time)
                    OptionalNumberField("Ounces", value: $draft.ounces)
                    Stepper(value: Binding(get: { draft.sugarCount ?? 0 }, set: { draft.sugarCount = $0 == 0 ? nil : $0 }), in: 0...10) {
                        LabeledContent("Sugar cubes / tsp", value: "\(draft.sugarCount ?? 0)")
                    }
                }
                ItemsSection(items: $draft.items, base: { draft.baseItem }, onChange: { draft.setTotals() })
                MacrosSection(macros: $draft.macros, locked: !draft.items.isEmpty)
                if draft.id != nil {
                    Section { Button("Delete drink", role: .destructive) { confirmDelete = true } }
                }
            }
            .navigationTitle(draft.id == nil ? "Add drink" : "Edit drink")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") { store.save(draft); dismiss() }
                        .disabled(draft.description.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
            .confirmationDialog("Delete this drink?", isPresented: $confirmDelete, titleVisibility: .visible) {
                Button("Delete", role: .destructive) {
                    if let id = draft.id.flatMap(UUID.init(uuidString:)) { store.deleteDrink(id) }
                    dismiss()
                }
            }
        }
    }
}

/// Text field with saved-food suggestions underneath while typing.
struct FoodNameField: View {
    @EnvironmentObject private var store: Store
    let title: String
    @Binding var text: String
    var picked: (String) -> Void = { _ in }
    @FocusState private var focused: Bool

    var body: some View {
        TextField(title, text: $text).focused($focused).submitLabel(.done)
        if focused {
            let sug = store.library.suggest(text).filter { $0.caseInsensitiveCompare(text) != .orderedSame }
            ForEach(sug.prefix(5), id: \.self) { name in
                Button { text = name; focused = false; picked(name) } label: {
                    Label(name, systemImage: "magnifyingglass").foregroundStyle(.primary)
                }
            }
        }
    }
}

/// The item breakdown: tap an item to edit its per-serving macros and servings; swipe to remove.
struct ItemsSection: View {
    @EnvironmentObject private var store: Store
    @Binding var items: [MealItem]
    let base: () -> MealItem?
    let onChange: () -> Void
    @State private var adding = ""
    @State private var editing: MealItem?

    var body: some View {
        Section {
            ForEach(items) { it in
                Button { editing = it } label: {
                    HStack {
                        VStack(alignment: .leading) {
                            Text(it.name).foregroundStyle(.primary)
                            if it.portion != 1 { Text("\(it.portion.formatted()) servings").font(.caption).foregroundStyle(.secondary) }
                        }
                        Spacer()
                        Text("\(it.kcal) cal").foregroundStyle(.secondary).monospacedDigit()
                    }
                }
            }
            .onDelete { idx in items.remove(atOffsets: idx); onChange() }
            HStack {
                TextField("Add an item (e.g. whole milk)", text: $adding).submitLabel(.done).onSubmit(add)
                Button("Add", action: add).disabled(adding.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            let sug = store.library.suggest(adding)
            if !sug.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        ForEach(sug, id: \.self) { s in
                            Button(s) { adding = s; add() }.buttonStyle(.bordered).controlSize(.small)
                        }
                    }
                }
            }
        } header: {
            Text("Items")
        } footer: {
            if !items.isEmpty { Text("Totals below are the sum of these items.") }
        }
        .sheet(item: $editing) { it in
            ItemEditor(item: it) { updated in
                if let i = items.firstIndex(where: { $0.id == updated.id }) { items[i] = updated; onChange() }
            }
        }
    }

    private func add() {
        let name = adding.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        if items.isEmpty, let b = base() { items.append(b) }   // keep totals typed before the first item
        items.append(store.library.item(for: name))
        adding = ""
        onChange()
    }
}

struct ItemEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    @State var item: MealItem
    @State private var alsoSave = true
    let apply: (MealItem) -> Void

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name", text: $item.name)
                    Stepper(value: $item.portion, in: 0.25...20, step: 0.25) {
                        LabeledContent("Servings", value: item.portion.formatted())
                    }
                }
                Section {
                    MacroFields(macros: Binding(get: { Macros.of(item) }, set: { m in
                        item.calories = m.calories; item.proteinG = m.proteinG; item.carbsG = m.carbsG; item.sugarG = m.sugarG
                        item.addedSugarG = m.addedSugarG; item.fatG = m.fatG; item.sodiumMg = m.sodiumMg; item.fiberG = m.fiberG
                    }))
                } header: { Text("Per serving") } footer: { Text("Servings multiply these.") }
                Section { Toggle("Also update saved food", isOn: $alsoSave) }
            }
            .navigationTitle(item.name.isEmpty ? "Item" : item.name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") {
                        apply(item)
                        if alsoSave { store.saveFood(item) }
                        dismiss()
                    }
                }
            }
        }
        .presentationDetents([.large])
    }
}

struct MacrosSection: View {
    @Binding var macros: Macros
    let locked: Bool
    var body: some View {
        Section {
            MacroFields(macros: $macros).disabled(locked)
        } header: { Text("Totals") } footer: {
            if locked { Text("Edit the items to change these.") }
        }
    }
}

struct MacroFields: View {
    @Binding var macros: Macros
    var body: some View {
        OptionalIntField("Calories", value: $macros.calories)
        OptionalNumberField("Protein (g)", value: $macros.proteinG)
        OptionalNumberField("Carbs (g)", value: $macros.carbsG)
        OptionalNumberField("Fat (g)", value: $macros.fatG)
        OptionalNumberField("Fiber (g)", value: $macros.fiberG)
        OptionalNumberField("Sugar (g)", value: $macros.sugarG)
        OptionalNumberField("Added sugar (g)", value: $macros.addedSugarG)
        OptionalIntField("Sodium (mg)", value: $macros.sodiumMg)
    }
}

/// Label on the left, right-aligned number on the right; blank = nil.
struct OptionalNumberField: View {
    let label: String
    @Binding var value: Double?
    init(_ label: String, value: Binding<Double?>) { self.label = label; _value = value }
    var body: some View {
        LabeledContent(label) {
            TextField("—", value: $value, format: .number.precision(.fractionLength(0...1)))
                .keyboardType(.decimalPad).multilineTextAlignment(.trailing).monospacedDigit()
        }
    }
}

struct OptionalIntField: View {
    let label: String
    @Binding var value: Int?
    init(_ label: String, value: Binding<Int?>) { self.label = label; _value = value }
    var body: some View {
        LabeledContent(label) {
            TextField("—", value: $value, format: .number)
                .keyboardType(.numberPad).multilineTextAlignment(.trailing).monospacedDigit()
        }
    }
}

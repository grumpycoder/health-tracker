import PhotosUI
import SwiftUI

/// Add or edit a meal. With items, the totals are their sum; without, totals are typed directly.
struct MealEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    @State var draft: MealDraft
    @State private var confirmDelete = false
    @State private var editingItem: MealItem?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    FoodNameField(title: "What did you eat?", text: $draft.description) { name in
                        // Picking a known meal on a new, empty entry repeats last time — items included.
                        guard draft.id == nil, draft.items.isEmpty, draft.macros.isEmpty else { return }
                        draft.repeatLast(name, from: store.library)
                    }
                    Picker("Meal", selection: $draft.mealType) {
                        ForEach(MealType.allCases.filter { $0 != .drink }, id: \.self) { Label($0.label, systemImage: $0.symbol).tag($0) }
                    }
                    DatePicker("Time", selection: $draft.time)
                    TextField("Portion note (optional)", text: $draft.portionNote)
                }
                ItemsSection(items: $draft.items, editing: $editingItem, base: { draft.baseItem }, onChange: { draft.setTotals() },
                             onLabel: { f in if draft.description.isEmpty, let d = f.description { draft.description = d } })
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
            .itemEditorSheet($editingItem) { updated in
                if let i = draft.items.firstIndex(where: { $0.id == updated.id }) { draft.items[i] = updated; draft.setTotals() }
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
    @State private var editingItem: MealItem?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    FoodNameField(title: "Drink", text: $draft.description) { name in
                        guard draft.id == nil, draft.items.isEmpty, draft.macros.isEmpty else { return }
                        draft.repeatLast(name, from: store.library)
                    }
                    DatePicker("Time", selection: $draft.time)
                    OptionalNumberField("Ounces", value: $draft.ounces)
                    Stepper(value: Binding(get: { draft.sugarCount ?? 0 }, set: { draft.sugarCount = $0 == 0 ? nil : $0 }), in: 0...10) {
                        LabeledContent("Sugar cubes / tsp", value: "\(draft.sugarCount ?? 0)")
                    }
                }
                ItemsSection(items: $draft.items, editing: $editingItem, base: { draft.baseItem }, onChange: { draft.setTotals() },
                             servings: { AIClient.servings(drankOunces: draft.ounces, servingSize: $0.servingSize) },
                             onLabel: { f in if draft.description.isEmpty, let d = f.description { draft.description = d } })
                MacrosSection(macros: $draft.macros, locked: !draft.items.isEmpty)
                if draft.id != nil {
                    Section { Button("Delete drink", role: .destructive) { confirmDelete = true } }
                }
            }
            .itemEditorSheet($editingItem) { updated in
                if let i = draft.items.firstIndex(where: { $0.id == updated.id }) { draft.items[i] = updated; draft.setTotals() }
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
    /// Item open in the editor sheet — presented by the parent editor, not from inside the Form.
    @Binding var editing: MealItem?
    let base: () -> MealItem?
    let onChange: () -> Void
    /// Servings to log for a scanned label (drinks: ounces drunk ÷ label serving); default 1.
    var servings: (AIClient.LabelFacts) -> Double = { _ in 1 }
    var onLabel: (AIClient.LabelFacts) -> Void = { _ in }
    @State private var adding = ""
    @State private var photo: PhotosPickerItem?
    @State private var scanning = false
    @State private var scanMessage: String?

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
            HStack {
                Button { CameraPresenter.present { image in Task { await scan(image) } } } label: {
                    Label("Scan label", systemImage: "camera")
                }
                    .disabled(scanning || !UIImagePickerController.isSourceTypeAvailable(.camera))
                Spacer()
                PhotosPicker(selection: $photo, matching: .images) { Label("Photo", systemImage: "photo") }
                    .disabled(scanning)
            }
            .buttonStyle(.borderless)
            if scanning { HStack { ProgressView(); Text("Reading label…").foregroundStyle(.secondary) } }
            if let m = scanMessage { Text(m).font(.footnote).foregroundStyle(.secondary) }
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
        .onChange(of: photo) { _, item in
            guard let item else { return }
            Task {
                photo = nil
                if let data = try? await item.loadTransferable(type: Data.self), let img = UIImage(data: data) { await scan(img) }
            }
        }
    }

    private func scan(_ image: UIImage) async {
        scanning = true; scanMessage = nil
        defer { scanning = false }
        do {
            guard let f = try await store.ai.readLabel(image, prompt: store.promptOverride("read_label")) else {
                scanMessage = "Couldn't read a nutrition label from that photo."; return
            }
            let name = (f.description?.trimmingCharacters(in: .whitespaces)).flatMap { $0.isEmpty ? nil : $0 } ?? "Item"
            let item = f.macros.item(named: name, portion: servings(f))
            if items.isEmpty, let b = base() { items.append(b) }
            items.append(item)
            onChange()
            onLabel(f)
            scanMessage = "Added \(name)" + (f.servingSize.map { " (label serving: \($0))" } ?? "") + " ✓"
            editing = item   // review what was read; "Also update saved food" saves it to the library
        } catch {
            scanMessage = "Scan failed: \(error.localizedDescription)"
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

extension View {
    /// The item editor sheet, attached at the editor level.
    func itemEditorSheet(_ item: Binding<MealItem?>, apply: @escaping (MealItem) -> Void) -> some View {
        sheet(item: item) { it in ItemEditor(item: it, apply: apply) }
    }
}

/// Presents the system camera imperatively from the top-most view controller. Presenting it
/// from SwiftUI inside a Form row let a re-render dismiss it before a photo could be taken.
@MainActor
enum CameraPresenter {
    private static var delegate: Delegate?

    static func present(_ picked: @escaping (UIImage) -> Void) {
        guard UIImagePickerController.isSourceTypeAvailable(.camera), let top = topController() else { return }
        let picker = UIImagePickerController()
        picker.sourceType = .camera
        let d = Delegate { image in
            picker.dismiss(animated: true)
            delegate = nil
            if let image { picked(image) }
        }
        delegate = d
        picker.delegate = d
        top.present(picker, animated: true)
    }

    private static func topController() -> UIViewController? {
        let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
            .first { $0.activationState == .foregroundActive }
        var top = scene?.windows.first { $0.isKeyWindow }?.rootViewController
        while let next = top?.presentedViewController { top = next }
        return top
    }

    private final class Delegate: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {
        let done: (UIImage?) -> Void
        init(_ done: @escaping (UIImage?) -> Void) { self.done = done }
        func imagePickerController(_ picker: UIImagePickerController, didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
            done(info[.originalImage] as? UIImage)
        }
        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) { done(nil) }
    }
}

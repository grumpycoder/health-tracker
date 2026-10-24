import SwiftUI

struct RoutinesView: View {
    @EnvironmentObject private var store: Store
    @State private var newName = ""
    @State private var adding = false
    @State private var showArchived = false

    var body: some View {
        List {
            Section {
                ForEach(store.routines.filter { !$0.isArchived }) { r in
                    NavigationLink(r.name) { RoutineDetailView(routineId: r.id) }
                }
            }
            let archived = store.routines.filter(\.isArchived)
            if !archived.isEmpty {
                Section(isExpanded: $showArchived) {
                    ForEach(archived) { r in NavigationLink(r.name) { RoutineDetailView(routineId: r.id) }.foregroundStyle(.secondary) }
                } header: { Text("Archived (\(archived.count))") }
            }
        }
        .listStyle(.sidebar)
        .navigationTitle("Routines")
        .toolbar { Button { adding = true } label: { Image(systemName: "plus") }.accessibilityLabel("New routine") }
        .alert("New routine", isPresented: $adding) {
            TextField("Name", text: $newName)
            Button("Create") { store.createRoutine(newName); newName = "" }
            Button("Cancel", role: .cancel) { newName = "" }
        }
    }
}

struct RoutineDetailView: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let routineId: UUID
    @State private var name = ""
    @State private var addName = ""
    @State private var addMeasure: ExerciseMeasure = .reps
    @State private var editing: RoutineExercise?
    @State private var message: String?

    private var routine: WorkoutRoutine? { store.routines.first { $0.id == routineId } }
    private var slots: [RoutineExercise] { store.routineExercises.filter { $0.routineId == routineId }.sorted { $0.order < $1.order } }
    private func exName(_ s: RoutineExercise) -> String { store.exercises.first { $0.id == s.exerciseDefinitionId }?.name ?? "Exercise" }

    var body: some View {
        if let routine {
            List {
                Section {
                    TextField("Name", text: $name).onSubmit(rename).onAppear { name = routine.name }
                    Picker("Order", selection: Binding(get: { routine.mode }, set: { store.updateRoutine(routineId, ["executionMode": .int(Int64($0.rawValue))]) })) {
                        ForEach(ExecutionMode.allCases, id: \.self) { Text($0.label).tag($0) }
                    }
                }
                Section {
                    ForEach(slots) { s in
                        Button { editing = s } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(exName(s)).foregroundStyle(.primary)
                                Text(prescription(s)).font(.caption).foregroundStyle(.secondary)
                            }
                        }
                    }
                    .onMove { from, to in var o = slots; o.move(fromOffsets: from, toOffset: to); store.reorder(routineId, o) }
                    .onDelete { idx in
                        var o = slots; let removed = idx.map { o[$0].id }; o.remove(atOffsets: idx)
                        store.reorder(routineId, o, removing: removed)
                    }
                    let typed = addName.trimmingCharacters(in: .whitespaces)
                    let known = store.exercises.first { $0.name.caseInsensitiveCompare(typed) == .orderedSame }
                    HStack {
                        TextField("Add exercise", text: $addName).submitLabel(.done).onSubmit(add)
                        Button("Add", action: add).disabled(typed.isEmpty)
                    }
                    if !typed.isEmpty && known == nil {
                        Picker("New exercise measured by", selection: $addMeasure) {
                            Text("Reps").tag(ExerciseMeasure.reps); Text("Time").tag(ExerciseMeasure.duration)
                        }
                        .pickerStyle(.segmented)
                    }
                    let sug = store.exercises.filter { !$0.isRetired && !typed.isEmpty && $0.name.localizedCaseInsensitiveContains(typed) && $0.name.caseInsensitiveCompare(typed) != .orderedSame }.prefix(5)
                    ForEach(Array(sug)) { e in Button(e.name) { addName = e.name; add() } }
                } header: { Text("Exercises") } footer: { Text("Drag to reorder (tap Edit), swipe to remove.") }
                Section {
                    Button(routine.isArchived ? "Restore routine" : "Archive routine") {
                        store.updateRoutine(routineId, ["archived": .bool(!routine.isArchived)])
                    }
                    Button("Delete routine", role: .destructive) {
                        if store.deleteRoutine(routineId) { dismiss() } else { message = "This routine has workouts in its history — archive it instead." }
                    }
                    if let m = message { Text(m).font(.footnote).foregroundStyle(.secondary) }
                }
            }
            .navigationTitle(routine.name)
            .toolbar { EditButton() }
            .sheet(item: $editing) { s in SlotEditor(slot: s, name: exName(s), isDuration: store.exercises.first { $0.id == s.exerciseDefinitionId }?.measure == .duration) }
        }
    }

    private func rename() {
        let n = name.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty, n != routine?.name else { return }
        store.updateRoutine(routineId, ["name": .string(n)])
    }

    private func add() {
        store.addExercise(addName, measure: addMeasure, to: routineId)
        addName = ""
    }

    private func prescription(_ s: RoutineExercise) -> String {
        var p = ["\(s.targetSets ?? 1) × " + (s.targetDurationSeconds.map { "\($0)s" } ?? "\(s.targetReps ?? 0)")]
        if let w = s.targetWeight { p.append("\(w.formatted()) lb") }
        if let r = s.restSeconds { p.append("rest \(r)s") }
        if let n = s.targetNote, !n.isEmpty { p.append(n) }
        return p.joined(separator: " · ")
    }
}

/// Per-routine targets for one exercise. Values can't be negative (C# ExercisePrescription).
struct SlotEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let slot: RoutineExercise
    let name: String
    let isDuration: Bool
    @State private var sets = 3
    @State private var reps = 10
    @State private var duration = 30
    @State private var rest = 60
    @State private var weight: Double?
    @State private var note = ""

    var body: some View {
        NavigationStack {
            Form {
                Stepper(value: $sets, in: 1...20) { LabeledContent("Sets", value: "\(sets)") }
                if isDuration {
                    Stepper(value: $duration, in: 5...3600, step: 5) { LabeledContent("Hold", value: "\(duration) s") }
                } else {
                    Stepper(value: $reps, in: 1...200) { LabeledContent("Reps", value: "\(reps)") }
                }
                Stepper(value: $rest, in: 0...600, step: 15) { LabeledContent("Rest", value: "\(rest) s") }
                LabeledContent("Weight (lb)") {
                    PadNumberField("Bodyweight", value: $weight)
                        .multilineTextAlignment(.trailing)
                }
                TextField("Note (e.g. 10-12, each side)", text: $note)
            }
            .navigationTitle(name)
            .navigationBarTitleDisplayMode(.inline)
            .onAppear {
                sets = slot.targetSets ?? 1; reps = slot.targetReps ?? 10; duration = slot.targetDurationSeconds ?? 30
                rest = slot.restSeconds ?? 60; weight = slot.targetWeight; note = slot.targetNote ?? ""
            }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        let n = note.trimmingCharacters(in: .whitespaces)
                        store.updateSlot(slot.id, [
                            "targetSets": .i(sets), "targetReps": isDuration ? .null : .i(reps),
                            "targetDurationSeconds": isDuration ? .i(duration) : .null, "restSeconds": .i(rest),
                            "targetWeight": .d(weight.map { max(0, $0) }), "targetNote": .s(n.isEmpty ? nil : n),
                        ])
                        dismiss()
                    }
                }
            }
        }
        .presentationDetents([.medium, .large])
    }
}

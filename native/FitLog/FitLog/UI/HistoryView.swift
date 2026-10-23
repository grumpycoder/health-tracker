import SwiftUI

struct HistoryView: View {
    @EnvironmentObject private var store: Store

    private var recent: [WorkoutSession] {
        Array(store.sessions.sorted { ($0.date, $0.startedAt ?? .distantPast) > ($1.date, $1.startedAt ?? .distantPast) }.prefix(100))
    }

    var body: some View {
        List {
            ForEach(recent, id: \.id) { s in
                if let id = s.id {
                    NavigationLink { SessionDetailView(sessionId: id) } label: { row(s, id) }
                        .swipeActions { Button("Delete", role: .destructive) { store.deleteSession(id) } }
                }
            }
        }
        .navigationTitle("History")
    }

    private func row(_ s: WorkoutSession, _ id: UUID) -> some View {
        let exercises = Set(store.sets.filter { $0.workoutSessionId == id }.map(\.exerciseDefinitionId)).count
        return VStack(alignment: .leading, spacing: 2) {
            Text(store.routines.first { $0.id == s.routineId }?.name ?? "Workout")
            Text([s.date.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day()),
                  duration(s.totalSeconds), "\(exercises) exercise\(exercises == 1 ? "" : "s")"].joined(separator: " · "))
                .font(.caption).foregroundStyle(.secondary)
        }
    }
}

func duration(_ seconds: Int?) -> String {
    guard let s = seconds, s > 0 else { return "—" }
    let m = s / 60
    return m >= 60 ? "\(m / 60)h \(m % 60)m" : "\(m) min"
}

/// Edit a past session's minutes, notes and set values (feedback is read-only, like the MAUI app).
struct SessionDetailView: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let sessionId: UUID
    @State private var minutes = 0
    @State private var notes = ""
    @State private var edits: [UUID: (reps: Int?, duration: Int?, weight: Double?, done: Bool)] = [:]
    @State private var loaded = false

    private var session: WorkoutSession? { store.sessions.first { $0.id == sessionId } }
    private var sets: [ExerciseSet] { store.sets.filter { $0.workoutSessionId == sessionId } }

    var body: some View {
        Form {
            Section {
                Stepper(value: $minutes, in: 0...600) { LabeledContent("Duration", value: "\(minutes) min") }
                TextField("Notes", text: $notes, axis: .vertical)
            }
            let groups = Dictionary(grouping: sets, by: \.exerciseDefinitionId)
            ForEach(groups.keys.sorted { name($0) < name($1) }, id: \.self) { ex in
                Section {
                    ForEach(groups[ex]!.sorted { $0.setNumber < $1.setNumber }) { s in setRow(s, isDuration: store.exercises.first { $0.id == ex }?.measure == .duration) }
                    if let f = store.feedback.first(where: { $0.workoutSessionId == sessionId && $0.exerciseDefinitionId == ex }) {
                        let flags = [f.difficulty != .unset ? f.difficulty.label : nil, f.painOrDiscomfort ? "Pain" : nil,
                                     f.breathingDifficulty ? "Breathing" : nil, f.formIssues ? "Form" : nil, f.comment].compactMap { $0 }
                        if !flags.isEmpty { Text(flags.joined(separator: " · ")).font(.footnote).foregroundStyle(.secondary) }
                    }
                } header: { Text(name(ex)) }
            }
        }
        .navigationTitle(store.routines.first { $0.id == session?.routineId }?.name ?? "Workout")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            guard !loaded, let s = session else { return }
            loaded = true
            minutes = (s.totalSeconds ?? 0) / 60
            notes = s.notes ?? ""
            for x in sets { edits[x.id] = (x.reps, x.durationSeconds, x.weight, x.completed) }
        }
        .toolbar {
            Button("Save") {
                let n = notes.trimmingCharacters(in: .whitespacesAndNewlines)
                let changed = sets.compactMap { s -> (UUID, Row)? in
                    guard let e = edits[s.id], e.reps != s.reps || e.duration != s.durationSeconds || e.weight != s.weight || e.done != s.completed else { return nil }
                    return (s.id, ["reps": .i(e.reps.map { max(0, $0) }), "durationSeconds": .i(e.duration.map { max(0, $0) }),
                                   "weight": .d(e.weight.map { max(0, $0) }), "completed": .bool(e.done)])
                }
                store.updateSession(sessionId, ["totalSeconds": .i(max(0, minutes) * 60), "notes": .s(n.isEmpty ? nil : n)], sets: changed)
                dismiss()
            }
        }
    }

    private func name(_ id: UUID) -> String { store.exercises.first { $0.id == id }?.name ?? "Exercise" }

    @ViewBuilder private func setRow(_ s: ExerciseSet, isDuration: Bool) -> some View {
        let e = edits[s.id] ?? (s.reps, s.durationSeconds, s.weight, s.completed)
        HStack {
            Text("Set \(s.setNumber)").foregroundStyle(.secondary)
            Spacer()
            TextField(isDuration ? "sec" : "reps", value: Binding(get: { isDuration ? e.duration : e.reps },
                                                                   set: { v in var x = e; if isDuration { x.duration = v } else { x.reps = v }; edits[s.id] = x }),
                      format: .number)
                .keyboardType(.numberPad).multilineTextAlignment(.trailing).frame(width: 56)
            Text(isDuration ? "s" : "reps").font(.caption).foregroundStyle(.secondary)
            TextField("lb", value: Binding(get: { e.weight }, set: { v in var x = e; x.weight = v; edits[s.id] = x }),
                      format: .number.precision(.fractionLength(0...1)))
                .keyboardType(.decimalPad).multilineTextAlignment(.trailing).frame(width: 56)
            Text("lb").font(.caption).foregroundStyle(.secondary)
            Toggle("Done", isOn: Binding(get: { e.done }, set: { v in var x = e; x.done = v; edits[s.id] = x })).labelsHidden()
        }
        .monospacedDigit()
    }
}

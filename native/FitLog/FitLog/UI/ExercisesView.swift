import SwiftUI

/// The exercise library: every exercise routines can use. Retired ones are hidden from routine
/// pickers but kept for history (listed last).
struct ExercisesView: View {
    @EnvironmentObject private var store: Store
    @State private var search = ""
    @State private var adding = false

    private var shown: [ExerciseDefinition] {
        store.exercises
            .filter { search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) }
            .sorted { ($0.isRetired ? 1 : 0, $0.name.lowercased()) < ($1.isRetired ? 1 : 0, $1.name.lowercased()) }
    }

    var body: some View {
        List(shown) { e in
            NavigationLink { ExerciseDetailView(exerciseId: e.id) } label: {
                VStack(alignment: .leading, spacing: 2) {
                    Text(e.name).foregroundStyle(e.isRetired ? .secondary : .primary)
                    Text(([e.measure == .duration ? "Timed" : "Reps"] + e.muscles + (e.isRetired ? ["retired"] : [])).joined(separator: " · "))
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        }
        .searchable(text: $search)
        .navigationTitle("Exercises")
        .toolbar { Button { adding = true } label: { Image(systemName: "plus") }.accessibilityLabel("New exercise") }
        .sheet(isPresented: $adding) { NewExerciseSheet() }
    }
}

struct NewExerciseSheet: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var measure: ExerciseMeasure = .reps
    @State private var warn: String?

    var body: some View {
        NavigationStack {
            Form {
                TextField("Name", text: $name)
                Picker("Measured by", selection: $measure) {
                    Text("Reps").tag(ExerciseMeasure.reps); Text("Time").tag(ExerciseMeasure.duration)
                }
                .pickerStyle(.segmented)
                if let w = warn { Text(w).foregroundStyle(.red) }
            }
            .navigationTitle("New exercise")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Add") { if let w = store.createExercise(name, measure: measure) { warn = w } else { dismiss() } }
                }
            }
        }
        .presentationDetents([.medium])
    }
}

struct ExerciseDetailView: View {
    @EnvironmentObject private var store: Store
    let exerciseId: UUID
    @State private var name = ""
    @State private var video = ""
    @State private var muscles: Set<String> = []
    @State private var retired = false
    @State private var loaded = false
    @State private var message: String?

    static let muscleCatalog = ["Chest", "Back", "Shoulders", "Biceps", "Triceps", "Forearms", "Core", "Glutes", "Quads", "Hamstrings", "Calves"]

    private var exercise: ExerciseDefinition? { store.exercises.first { $0.id == exerciseId } }

    var body: some View {
        if let e = exercise {
            Form {
                Section {
                    TextField("Name", text: $name)
                    LabeledContent("Measured by", value: e.measure == .duration ? "Time" : "Reps")
                    TextField("Demo video URL (optional)", text: $video).keyboardType(.URL).textInputAutocapitalization(.never)
                    Link(destination: demoURL(e)) { Label(video.isEmpty ? "Search form videos" : "Open demo", systemImage: "play.rectangle") }
                }
                Section("Muscle groups") {
                    ForEach(Self.muscleCatalog, id: \.self) { m in
                        Button { if muscles.contains(m) { muscles.remove(m) } else { muscles.insert(m) } } label: {
                            HStack { Text(m).foregroundStyle(.primary); Spacer(); if muscles.contains(m) { Image(systemName: "checkmark").foregroundStyle(.tint) } }
                        }
                    }
                }
                Section {
                    Toggle("Retired (hidden from routine pickers)", isOn: $retired)
                } footer: { Text(usage(e)) }
                if let m = message { Section { Text(m).foregroundStyle(m.hasSuffix("✓") ? Color.secondary : Color.red) } }
            }
            .navigationTitle(e.name)
            .navigationBarTitleDisplayMode(.inline)
            .onAppear {
                guard !loaded else { return }
                loaded = true
                name = e.name; video = e.videoUrl ?? ""; retired = e.isRetired
                muscles = Set(e.muscles)
            }
            .toolbar {
                Button("Save") {
                    // Keep catalog order for the CSV, like the other apps.
                    let ordered = Self.muscleCatalog.filter(muscles.contains) + muscles.subtracting(Self.muscleCatalog).sorted()
                    message = store.updateExercise(exerciseId, name: name, videoUrl: video, muscles: ordered, retired: retired) ?? "Saved ✓"
                }
            }
        }
    }

    private func demoURL(_ e: ExerciseDefinition) -> URL {
        if let u = URL(string: video.trimmingCharacters(in: .whitespaces)), u.scheme?.hasPrefix("http") == true { return u }
        let q = "\(e.name) exercise form".addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? ""
        return URL(string: "https://www.youtube.com/results?search_query=\(q)")!
    }

    private func usage(_ e: ExerciseDefinition) -> String {
        let routines = Set(store.routineExercises.filter { $0.exerciseDefinitionId == e.id }.map(\.routineId)).count
        let sessions = Set(store.sets.filter { $0.exerciseDefinitionId == e.id }.map(\.workoutSessionId)).count
        return "In \(routines) routine\(routines == 1 ? "" : "s") · performed in \(sessions) workout\(sessions == 1 ? "" : "s")"
    }
}

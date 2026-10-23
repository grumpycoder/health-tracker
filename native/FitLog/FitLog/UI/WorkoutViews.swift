import SwiftUI

struct WorkoutTab: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var workout: WorkoutController

    var body: some View {
        NavigationStack {
            if workout.run != nil {
                LiveWorkoutView()
            } else {
                List {
                    Section("Start a routine") {
                        let active = store.routines.filter { !$0.isArchived }
                        if active.isEmpty { Text("No routines yet.").foregroundStyle(.secondary) }
                        ForEach(active) { r in
                            Button { workout.start(r) } label: {
                                HStack {
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(r.name).foregroundStyle(.primary)
                                        Text(summary(r)).font(.caption).foregroundStyle(.secondary)
                                    }
                                    Spacer()
                                    Image(systemName: "play.circle.fill").font(.title2).foregroundStyle(.tint)
                                }
                            }
                        }
                    }
                    Section {
                        NavigationLink { RoutinesView() } label: { Label("Routines", systemImage: "list.bullet.rectangle") }
                        NavigationLink { HistoryView() } label: { Label("History", systemImage: "clock.arrow.circlepath") }
                    }
                }
                .navigationTitle("Workout")
            }
        }
    }

    private func summary(_ r: WorkoutRoutine) -> String {
        let n = store.routineExercises.filter { $0.routineId == r.id }.count
        let done = store.sessions.filter { $0.routineId == r.id }.count
        return "\(n) exercise\(n == 1 ? "" : "s") · \(done) workout\(done == 1 ? "" : "s") · \(r.mode.label)"
    }
}

struct LiveWorkoutView: View {
    @EnvironmentObject private var workout: WorkoutController
    @State private var confirmDiscard = false

    var body: some View {
        if let run = workout.run {
            if run.finishing { FinishWorkoutView() } else { live(run) }
        }
    }

    @ViewBuilder private func live(_ run: WorkoutRun) -> some View {
        List {
            if let rest = run.restEndsAt, rest > workout.now {
                Section {
                    HStack {
                        Label("Rest", systemImage: "timer").font(.headline)
                        Spacer()
                        Text(clock(Int(rest.timeIntervalSince(workout.now).rounded(.up)))).font(.title2.monospacedDigit().weight(.semibold))
                        Button("Skip") { workout.mutate { $0.restEndsAt = nil } }.buttonStyle(.bordered)
                    }
                }
            }
            if let step = run.current {
                currentSet(run, step)
            }
            Section("Progress") {
                ForEach(Array(run.exercises.enumerated()), id: \.offset) { i, e in
                    Button { workout.mutate { $0.jump(to: i) } } label: {
                        HStack {
                            Image(systemName: e.skipped ? "forward.fill" : e.sets.allSatisfy(\.completed) ? "checkmark.circle.fill" : "circle")
                                .foregroundStyle(e.skipped ? .orange : e.sets.allSatisfy(\.completed) ? .green : .secondary)
                            Text(e.name).foregroundStyle(.primary)
                            Spacer()
                            Text(e.skipped ? "skipped" : "\(e.sets.filter(\.completed).count)/\(e.sets.count)")
                                .foregroundStyle(.secondary).monospacedDigit()
                        }
                    }
                    .listRowBackground(run.current?.exercise == i ? Color.accentColor.opacity(0.12) : nil)
                }
            }
            Section {
                Button("Finish workout") { workout.mutate { $0.goToFinish() } }
                Button("Discard workout", role: .destructive) { confirmDiscard = true }
            }
        }
        .navigationTitle(run.routineName)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Label(clock(Int(workout.now.timeIntervalSince(run.startedAt))), systemImage: "stopwatch")
                    .labelStyle(.titleAndIcon).monospacedDigit()
            }
        }
        .onAppear { UIApplication.shared.isIdleTimerDisabled = true }
        .onDisappear { UIApplication.shared.isIdleTimerDisabled = false }
        .confirmationDialog("Discard this workout? Nothing will be saved.", isPresented: $confirmDiscard, titleVisibility: .visible) {
            Button("Discard", role: .destructive) { workout.discard() }
        }
    }

    @ViewBuilder private func currentSet(_ run: WorkoutRun, _ step: WorkoutRun.Step) -> some View {
        let ex = run.exercises[step.exercise]
        let set = ex.sets[step.set]
        Section {
            VStack(alignment: .leading, spacing: 4) {
                Text(ex.name).font(.title2.bold())
                Text("Set \(step.set + 1) of \(ex.sets.count)" + (ex.restSeconds > 0 ? " · rest \(ex.restSeconds)s" : ""))
                    .foregroundStyle(.secondary)
                if let note = ex.note, !note.isEmpty { Text(note).font(.callout).foregroundStyle(.secondary) }
            }
            .padding(.vertical, 4)
            if ex.isDuration {
                if let rem = run.holdRemaining(now: workout.now), run.hold?.step == step {
                    HStack {
                        Text(clock(rem)).font(.system(size: 44, weight: .semibold).monospacedDigit())
                        Spacer()
                        Button("Stop") { workout.mutate { $0.stopHold() } }.buttonStyle(.bordered)
                    }
                } else {
                    Stepper(value: binding(step, \.duration, default: 30), in: 0...3600, step: 5) {
                        LabeledContent("Hold", value: "\(set.duration ?? 0) s")
                    }
                    Button { workout.mutate { $0.startHold() } } label: { Label("Start hold", systemImage: "play.fill") }
                }
            } else {
                Stepper(value: binding(step, \.reps, default: 0), in: 0...200) {
                    LabeledContent("Reps", value: "\(set.reps ?? 0)")
                }
            }
            LabeledContent("Weight (lb)") {
                TextField("Bodyweight", value: weightBinding(step), format: .number.precision(.fractionLength(0...1)))
                    .keyboardType(.decimalPad).multilineTextAlignment(.trailing)
            }
            Button { workout.mutate { $0.completeCurrent() } } label: {
                Text(set.completed ? "Completed ✓ — next" : "Complete set").frame(maxWidth: .infinity).font(.headline)
            }
            .buttonStyle(.borderedProminent).controlSize(.large)
            .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
            HStack {
                Button { workout.mutate { $0.back() } } label: { Label("Back", systemImage: "chevron.left") }
                    .disabled(run.stepIndex == 0)
                Spacer()
                Button("Skip \(ex.name)") { workout.mutate { $0.skipCurrentExercise() } }.foregroundStyle(.orange)
            }
            .buttonStyle(.borderless)
        }
    }

    private func binding(_ s: WorkoutRun.Step, _ key: WritableKeyPath<WorkoutRun.RunSet, Int?>, default d: Int) -> Binding<Int> {
        Binding(get: { workout.run?.exercises[s.exercise].sets[s.set][keyPath: key] ?? d },
                set: { v in workout.mutate { $0.exercises[s.exercise].sets[s.set][keyPath: key] = v } })
    }
    private func weightBinding(_ s: WorkoutRun.Step) -> Binding<Double?> {
        Binding(get: { workout.run?.exercises[s.exercise].sets[s.set].weight },
                set: { v in workout.mutate { $0.exercises[s.exercise].sets[s.set].weight = v } })
    }
}

struct FinishWorkoutView: View {
    @EnvironmentObject private var workout: WorkoutController
    @State private var saving = false
    @State private var confirmDiscard = false

    var body: some View {
        if let run = workout.run {
            Form {
                Section {
                    Stepper(value: Binding(get: { workout.run?.minutes ?? 0 }, set: { v in workout.mutate { $0.minutes = v } }), in: 0...600) {
                        LabeledContent("Duration", value: "\(run.minutes ?? 0) min")
                    }
                    TextField("Notes (optional)", text: Binding(get: { workout.run?.notes ?? "" }, set: { v in workout.mutate { $0.notes = v } }), axis: .vertical)
                }
                ForEach(Array(run.exercises.enumerated()), id: \.offset) { i, e in
                    if !e.skipped {
                        Section(e.name) {
                            Picker("Difficulty", selection: Binding(get: { workout.run?.exercises[i].difficulty ?? 0 },
                                                                   set: { v in workout.mutate { $0.exercises[i].difficulty = v } })) {
                                ForEach(Difficulty.allCases) { Text($0.label).tag($0.rawValue) }
                            }
                            Toggle("Pain or discomfort", isOn: flag(i, \.pain))
                            Toggle("Breathing difficulty", isOn: flag(i, \.breathing))
                            Toggle("Form issues", isOn: flag(i, \.form))
                            TextField("Comment", text: Binding(get: { workout.run?.exercises[i].comment ?? "" },
                                                               set: { v in workout.mutate { $0.exercises[i].comment = v } }))
                        }
                    }
                }
                if let e = workout.error { Section { Text(e).foregroundStyle(.red) } }
                Section {
                    Button("Back to workout") { workout.mutate { $0.finishing = false } }
                    Button("Discard workout", role: .destructive) { confirmDiscard = true }
                }
            }
            .navigationTitle("Finish \(run.routineName)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button(saving ? "Saving…" : "Save") {
                        saving = true
                        Task { _ = await workout.save(); saving = false }
                    }
                    .disabled(saving)
                }
            }
            .confirmationDialog("Discard this workout? Nothing will be saved.", isPresented: $confirmDiscard, titleVisibility: .visible) {
                Button("Discard", role: .destructive) { workout.discard() }
            }
        }
    }

    private func flag(_ i: Int, _ key: WritableKeyPath<WorkoutRun.RunExercise, Bool>) -> Binding<Bool> {
        Binding(get: { workout.run?.exercises[i][keyPath: key] ?? false }, set: { v in workout.mutate { $0.exercises[i][keyPath: key] = v } })
    }
}

func clock(_ seconds: Int) -> String {
    let s = max(0, seconds)
    return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60) : String(format: "%d:%02d", s / 60, s % 60)
}

import Foundation

/// An in-progress workout. Codable and saved to disk on every change, so it survives the app being
/// closed (the MAUI app keeps it in memory only). Nothing reaches the synced store until Save.
struct WorkoutRun: Codable, Equatable {
    struct RunSet: Codable, Equatable {
        var reps: Int?
        var weight: Double?
        var duration: Int?
        var completed = false
    }
    struct RunExercise: Codable, Equatable, Identifiable {
        var id: UUID { definitionId }
        let definitionId: UUID
        let name: String
        let isDuration: Bool
        let restSeconds: Int
        let note: String?
        var skipped = false
        var sets: [RunSet]
        // Feedback, filled on the finish screen.
        var difficulty = 0
        var pain = false, breathing = false, form = false
        var comment = ""
    }
    struct Step: Codable, Equatable { let exercise: Int; let set: Int }
    struct Hold: Codable, Equatable { let step: Step; let startedAt: Date; let target: Int }

    var routineId: UUID?
    var routineName: String
    var startedAt = Date()
    var exercises: [RunExercise]
    var steps: [Step]
    var stepIndex = 0
    var restEndsAt: Date?
    var hold: Hold?
    var finishing = false
    var minutes: Int?
    var notes = ""

    /// Build from a routine, matching the MAUI app: sets pre-filled from the routine's targets only;
    /// straight sets (A1,A2,B1…) or circuit rounds (A1,B1,A2,B2…).
    init(routine: WorkoutRoutine, slots: [RoutineExercise], library: [UUID: ExerciseDefinition]) {
        routineId = routine.id
        routineName = routine.name
        let exercises: [RunExercise] = slots.sorted { $0.order < $1.order }.compactMap { slot in
            guard let def = library[slot.exerciseDefinitionId] else { return nil }
            let isDuration = def.measure == .duration
            let set = RunSet(reps: isDuration ? nil : slot.targetReps, weight: slot.targetWeight,
                             duration: isDuration ? slot.targetDurationSeconds : nil)
            return RunExercise(definitionId: def.id, name: def.name, isDuration: isDuration,
                               restSeconds: slot.restSeconds ?? 0, note: slot.targetNote,
                               sets: Array(repeating: set, count: max(1, slot.targetSets ?? 1)))
        }
        self.exercises = exercises
        if routine.mode == .circuit {
            let rounds = exercises.map(\.sets.count).max() ?? 0
            steps = (0..<rounds).flatMap { r in exercises.indices.compactMap { e in r < exercises[e].sets.count ? Step(exercise: e, set: r) : nil } }
        } else {
            steps = exercises.indices.flatMap { e in exercises[e].sets.indices.map { Step(exercise: e, set: $0) } }
        }
    }

    var current: Step? { steps.indices.contains(stepIndex) ? steps[stepIndex] : nil }
    var isDone: Bool { nextOpen(from: 0) == nil }

    func isOpen(_ s: Step) -> Bool { !exercises[s.exercise].skipped && !exercises[s.exercise].sets[s.set].completed }

    func nextOpen(from i: Int) -> Int? { steps.indices.first { $0 >= i && isOpen(steps[$0]) } }

    mutating func completeCurrent(now: Date = Date()) {
        guard let s = current else { return }
        stopHold(now: now)
        exercises[s.exercise].sets[s.set].completed = true
        if let next = nextOpen(from: stepIndex + 1) ?? nextOpen(from: 0) {
            stepIndex = next
            let rest = exercises[s.exercise].restSeconds
            restEndsAt = rest > 0 ? now.addingTimeInterval(TimeInterval(rest)) : nil
        } else {
            restEndsAt = nil
            finishing = true
        }
    }

    mutating func back() {
        stopHold(); restEndsAt = nil
        if stepIndex > 0 { stepIndex -= 1 }
    }

    /// Jump to an exercise's first open set (or its first set); tapping a skipped exercise un-skips it.
    mutating func jump(to e: Int) {
        stopHold(); restEndsAt = nil
        exercises[e].skipped = false
        stepIndex = steps.indices.first { steps[$0].exercise == e && isOpen(steps[$0]) }
            ?? steps.indices.first { steps[$0].exercise == e } ?? stepIndex
    }

    mutating func skipCurrentExercise() {
        guard let s = current else { return }
        stopHold(); restEndsAt = nil
        exercises[s.exercise].skipped = true
        if let next = nextOpen(from: stepIndex) ?? nextOpen(from: 0) { stepIndex = next } else { finishing = true }
    }

    // Holds (timed sets): target = the set's duration, else 30s. Never auto-completes.
    mutating func startHold(now: Date = Date()) {
        guard let s = current else { return }
        restEndsAt = nil
        let t = exercises[s.exercise].sets[s.set].duration ?? 0
        hold = Hold(step: s, startedAt: now, target: t > 0 ? t : 30)
    }

    /// Stop a running hold, recording min(elapsed, target).
    mutating func stopHold(now: Date = Date()) {
        guard let h = hold else { return }
        let elapsed = Int(now.timeIntervalSince(h.startedAt))
        exercises[h.step.exercise].sets[h.step.set].duration = min(elapsed, h.target)
        hold = nil
    }

    func holdRemaining(now: Date = Date()) -> Int? {
        hold.map { max(0, $0.target - Int(now.timeIntervalSince($0.startedAt))) }
    }

    mutating func goToFinish(now: Date = Date()) {
        stopHold(now: now); restEndsAt = nil
        if minutes == nil { minutes = Int((now.timeIntervalSince(startedAt) / 60).rounded(.toNearestOrEven)) }
        finishing = true
    }

    // MARK: persistence

    private static var url: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("active-workout.json")
    }
    static func load() -> WorkoutRun? { (try? Data(contentsOf: url)).flatMap { try? JSONDecoder().decode(WorkoutRun.self, from: $0) } }
    func save() { try? JSONEncoder().encode(self).write(to: Self.url, options: .atomic) }
    static func clear() { try? FileManager.default.removeItem(at: url) }
}

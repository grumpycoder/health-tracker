import Foundation
import UIKit
import UserNotifications

/// Runs the live workout: owns the persisted WorkoutRun, a 1-second tick for the timers, rest/hold
/// alerts, and Save — which writes rows in the same shape as the MAUI app's LogCompletedAsync.
@MainActor
final class WorkoutController: ObservableObject {
    @Published var run: WorkoutRun? { didSet { if let run { run.save() } else { WorkoutRun.clear() } } }
    @Published private(set) var now = Date()
    @Published var error: String?

    private let store: Store
    private var ticker: Timer?
    private var holdChimed = false
    private var restChimed = false

    init(store: Store) {
        self.store = store
        run = WorkoutRun.load()
        if run != nil { startTicking() }
    }

    func start(_ routine: WorkoutRoutine) {
        let slots = store.routineExercises.filter { $0.routineId == routine.id }
        let lib = Dictionary(uniqueKeysWithValues: store.exercises.map { ($0.id, $0) })
        run = WorkoutRun(routine: routine, slots: slots, library: lib)
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        startTicking()
    }

    func mutate(_ f: (inout WorkoutRun) -> Void) {
        guard var r = run else { return }
        let hadHold = r.hold != nil
        f(&r)
        run = r
        if r.hold == nil { cancel("hold"); holdChimed = false }
        if !hadHold, let h = r.hold { schedule("hold", "Hold complete ✅", "\(r.exercises[h.step.exercise].name) — \(h.target)s done", at: h.startedAt.addingTimeInterval(TimeInterval(h.target))) }
        if let rest = r.restEndsAt { restChimed = false; schedule("rest", "Rest over", "Next: \(nextName(r))", at: rest) } else { cancel("rest") }
    }

    func discard() { stop(); run = nil }

    // MARK: ticking

    private func startTicking() {
        ticker?.invalidate()
        ticker = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
    }

    private func stop() {
        ticker?.invalidate(); ticker = nil
        cancel("hold"); cancel("rest")
    }

    private func tick() {
        now = Date()
        guard var r = run else { return }
        if let rem = r.holdRemaining(now: now), rem == 0, !holdChimed {
            holdChimed = true
            r.stopHold(now: now)   // full hold reached: record the target; user still taps Complete
            run = r
            cancel("hold")
            UINotificationFeedbackGenerator().notificationOccurred(.success)
        }
        if let rest = r.restEndsAt, now >= rest, !restChimed {
            restChimed = true
            r.restEndsAt = nil
            run = r
            UINotificationFeedbackGenerator().notificationOccurred(.warning)
        }
    }

    private func nextName(_ r: WorkoutRun) -> String { r.current.map { r.exercises[$0.exercise].name } ?? "finish" }

    private func schedule(_ id: String, _ title: String, _ body: String, at date: Date) {
        let secs = date.timeIntervalSinceNow
        guard secs > 0.5 else { return }
        let c = UNMutableNotificationContent(); c.title = title; c.body = body; c.sound = .default
        let req = UNNotificationRequest(identifier: "workout.\(id)", content: c,
                                        trigger: UNTimeIntervalNotificationTrigger(timeInterval: secs, repeats: false))
        UNUserNotificationCenter.current().add(req)
    }
    private func cancel(_ id: String) { UNUserNotificationCenter.current().removePendingNotificationRequests(withIdentifiers: ["workout.\(id)"]) }

    // MARK: save

    /// Writes the session, ALL sets (completed or not, like the MAUI app) numbered 1..N per exercise,
    /// and one feedback row per exercise; then marks the day a Workout day and saves to Health.
    func save() async -> Bool {
        guard var r = run else { return false }
        r.stopHold()
        let end = Date()
        guard end >= r.startedAt else { error = "End time is before the start."; return false }
        for e in r.exercises {
            for s in e.sets where (s.reps ?? 0) < 0 || (s.weight ?? 0) < 0 || (s.duration ?? 0) < 0 {
                error = "\(e.name): values can't be negative."; return false
            }
        }
        let minutes = max(0, r.minutes ?? Int((end.timeIntervalSince(r.startedAt) / 60).rounded(.toNearestOrEven)))
        let note = r.notes.trimmingCharacters(in: .whitespacesAndNewlines)
        let ok = store.saveWorkout { rows in
            let sessionId = try rows.insert("WorkoutSession", [
                "date": Wire.day(Day(r.startedAt)), "routineId": .s(r.routineId?.uuidString.lowercased()),
                "startedAt": Wire.local(r.startedAt), "endedAt": Wire.local(end),
                "totalSeconds": .i(minutes * 60), "notes": .s(note.isEmpty ? nil : note),
            ])
            var numbers: [UUID: Int] = [:]
            var feedback: [UUID: Row] = [:]
            for e in r.exercises {
                for s in e.sets {
                    numbers[e.definitionId, default: 0] += 1
                    try rows.insert("ExerciseSet", [
                        "workoutSessionId": .string(sessionId), "exerciseDefinitionId": .string(e.definitionId.uuidString.lowercased()),
                        "setNumber": .i(numbers[e.definitionId]), "reps": .i(s.reps), "weight": .d(s.weight),
                        "durationSeconds": .i(s.duration), "restSeconds": .null, "completed": .bool(s.completed),
                    ], now: s.completedAt ?? end)   // createdAt = when it was done (history orders by it)
                }
                let c = e.comment.trimmingCharacters(in: .whitespacesAndNewlines)
                feedback[e.definitionId] = [   // one per exercise per session — last slot wins, like the domain
                    "workoutSessionId": .string(sessionId), "exerciseDefinitionId": .string(e.definitionId.uuidString.lowercased()),
                    "difficulty": .i(e.difficulty), "painOrDiscomfort": .bool(e.pain), "breathingDifficulty": .bool(e.breathing),
                    "formIssues": .bool(e.form), "comment": .s(c.isEmpty ? (e.skipped ? "Skipped" : nil) : c),
                ]
            }
            for f in feedback.values { try rows.insert("ExerciseFeedback", f) }
        }
        guard ok else { error = store.error; return false }
        stop()
        run = nil
        let start = r.startedAt, name = r.routineName
        Task {
            await store.markWorkoutDay(Day(start))
            await store.health.saveStrengthWorkout(start: start, end: end, name: name, weightLbs: store.latestWeightLbs)
        }
        return true
    }
}

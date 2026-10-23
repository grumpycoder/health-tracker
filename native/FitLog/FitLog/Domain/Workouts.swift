import Foundation

// Typed read views of the workout entities (FitRecoveryLog.Shared/Workouts). Writes go through
// RowStore as field merges, like meals.

enum ExerciseMeasure: Int, Decodable { case reps = 0, duration = 1 }
enum ExecutionMode: Int, Decodable, CaseIterable {
    case straightSets = 0, circuit = 1
    var label: String { self == .straightSets ? "Straight sets" : "Circuit" }
}
enum Difficulty: Int, Decodable, CaseIterable, Identifiable {
    case unset, easy, moderate, hard, veryHard
    var id: Int { rawValue }
    var label: String { ["—", "Easy", "Moderate", "Hard", "Very hard"][rawValue] }
}

struct ExerciseDefinition: Decodable, Identifiable {
    let id: UUID
    let name: String
    let measure: ExerciseMeasure
    let equipmentNotes, progressionNotes, videoUrl, muscleGroups: String?
    let retired: Bool?
    var isRetired: Bool { retired ?? false }
    var muscles: [String] { (muscleGroups ?? "").split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty } }
}

struct WorkoutRoutine: Decodable, Identifiable {
    let id: UUID
    let name: String
    let notes: String?
    let archived: Bool?
    let executionMode: ExecutionMode?
    var isArchived: Bool { archived ?? false }
    var mode: ExecutionMode { executionMode ?? .straightSets }
}

struct RoutineExercise: Decodable, Identifiable {
    let id: UUID
    let routineId: UUID
    let exerciseDefinitionId: UUID
    let order: Int
    let targetSets, targetReps, targetDurationSeconds, restSeconds: Int?
    let targetWeight: Double?
    let targetNote: String?
}

struct ExerciseSet: Decodable, Identifiable {
    let id: UUID
    let workoutSessionId: UUID
    let exerciseDefinitionId: UUID
    let setNumber: Int
    let reps: Int?
    let weight: Double?
    let durationSeconds, restSeconds: Int?
    let completed: Bool
}

struct ExerciseFeedback: Decodable, Identifiable {
    let id: UUID
    let workoutSessionId: UUID
    let exerciseDefinitionId: UUID
    let difficulty: Difficulty
    let painOrDiscomfort, breathingDifficulty, formIssues: Bool
    let comment: String?
}

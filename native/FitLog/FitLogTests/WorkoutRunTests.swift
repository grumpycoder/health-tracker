import XCTest
@testable import FitLog

final class WorkoutRunTests: XCTestCase {
    private func decode<T: Decodable>(_ json: String) -> T { try! Wire.decoder.decode(T.self, from: Data(json.utf8)) }
    private let a = UUID(), b = UUID(), r = UUID()

    private func run(_ mode: Int) -> WorkoutRun {
        let routine: WorkoutRoutine = decode(#"{"id":"\#(r)","name":"Push","executionMode":\#(mode)}"#)
        let lib: [UUID: ExerciseDefinition] = [
            a: decode(#"{"id":"\#(a)","name":"Push-ups","measure":0}"#),
            b: decode(#"{"id":"\#(b)","name":"Plank","measure":1}"#),
        ]
        let slots: [RoutineExercise] = [
            decode(#"{"id":"\#(UUID())","routineId":"\#(r)","exerciseDefinitionId":"\#(b)","order":2,"targetSets":2,"targetDurationSeconds":45,"restSeconds":30}"#),
            decode(#"{"id":"\#(UUID())","routineId":"\#(r)","exerciseDefinitionId":"\#(a)","order":1,"targetSets":3,"targetReps":10,"targetWeight":20,"restSeconds":60}"#),
        ]
        return WorkoutRun(routine: routine, slots: slots, library: lib)
    }

    func testPrefillFromTargetsAndStraightOrder() {
        let w = run(0)
        XCTAssertEqual(w.exercises.map(\.name), ["Push-ups", "Plank"])
        XCTAssertEqual(w.exercises[0].sets.first, .init(reps: 10, weight: 20, duration: nil))
        XCTAssertEqual(w.exercises[1].sets.first, .init(reps: nil, weight: nil, duration: 45), "duration exercise has no reps")
        XCTAssertEqual(w.steps.map { "\($0.exercise)\($0.set)" }, ["00", "01", "02", "10", "11"])
    }

    func testCompletingASetRecordsWhenItWasDone() {
        var w = run(0)
        let t1 = Date(timeIntervalSince1970: 1_000), t2 = Date(timeIntervalSince1970: 2_000)
        w.jump(to: 1)
        w.completeCurrent(now: t1)              // did the plank first
        w.jump(to: 0)
        w.completeCurrent(now: t2)
        XCTAssertEqual(w.exercises[1].sets[0].completedAt, t1)
        XCTAssertEqual(w.exercises[0].sets[0].completedAt, t2)
        XCTAssertNil(w.exercises[0].sets[1].completedAt)
    }

    func testCircuitOrder() {
        XCTAssertEqual(run(1).steps.map { "\($0.exercise)\($0.set)" }, ["00", "10", "01", "11", "02"])
    }

    func testCompleteStartsRestAndSkipAdvancesToFinish() {
        var w = run(0)
        let t = Date()
        w.completeCurrent(now: t)
        XCTAssertTrue(w.exercises[0].sets[0].completed)
        XCTAssertEqual(w.stepIndex, 1)
        XCTAssertEqual(w.restEndsAt, t.addingTimeInterval(60))
        w.skipCurrentExercise()
        XCTAssertEqual(w.current?.exercise, 1, "skipping push-ups jumps to the plank")
        w.skipCurrentExercise()
        XCTAssertTrue(w.finishing)
    }

    func testHoldRecordsElapsedCappedAtTarget() {
        var w = run(0)
        w.jump(to: 1)
        let t = Date()
        w.startHold(now: t)
        XCTAssertEqual(w.holdRemaining(now: t.addingTimeInterval(10)), 35)
        w.stopHold(now: t.addingTimeInterval(20))
        XCTAssertEqual(w.exercises[1].sets[0].duration, 20)
        w.startHold(now: t); w.stopHold(now: t.addingTimeInterval(500))
        XCTAssertEqual(w.exercises[1].sets[0].duration, 20, "second hold targets the recorded 20s and caps there")
    }

    func testSurvivesRoundTripToDisk() throws {
        let w = run(1)
        let data = try JSONEncoder().encode(w)
        XCTAssertEqual(try JSONDecoder().decode(WorkoutRun.self, from: data), w)
    }
}

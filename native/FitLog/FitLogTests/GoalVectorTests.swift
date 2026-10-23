import XCTest
@testable import FitLog

/// Runs tests/vectors/goals.json — the same cases the C# GoalVectorTests run.
final class GoalVectorTests: XCTestCase {
    private struct Expect: Decodable {
        let label: String; let percent: Int?; let met: Bool; let trend, arrow, status, achieved, recent, recentTrend: String
    }
    private struct Goals: Decodable { let goalWeightLbs, goalWaistInches, goalBodyFatPercent: Double? }
    private struct File: Decodable { let goals: Goals; let history: [BodyMeasurement]; let expect: [Expect] }

    func testOverviewMatchesSharedVectors() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "goals", withExtension: "json"))
        let f = try Wire.decoder.decode(File.self, from: Data(contentsOf: url))
        let g: GoalSettings = try Wire.decoder.decode(GoalSettings.self, from: Data("""
            {"updatedAt":"2026-10-01T00:00:00Z","proteinMin":0,"proteinMax":0,"fatMin":0,"fatMax":0,"fiberMin":0,"fiberMax":0,
             "caloriesRestMin":0,"caloriesRestMax":0,"caloriesActiveMin":0,"caloriesActiveMax":0,"carbsRestMin":0,"carbsRestMax":0,
             "carbsActiveMin":0,"carbsActiveMax":0,"goalWeightLbs":\(f.goals.goalWeightLbs!),"goalWaistInches":\(f.goals.goalWaistInches!),
             "goalBodyFatPercent":\(f.goals.goalBodyFatPercent!)}
            """.utf8))
        let rows = GoalProgress.overview(g, f.history)
        for e in f.expect {
            let r = try XCTUnwrap(rows.first { $0.label == e.label })
            XCTAssertEqual(r.percent, e.percent, e.label); XCTAssertEqual(r.met, e.met, e.label)
            XCTAssertEqual("\(r.trend)".lowercased(), e.trend, e.label); XCTAssertEqual(r.arrow, e.arrow, e.label)
            XCTAssertEqual(r.status, e.status, e.label); XCTAssertEqual(r.achieved, e.achieved, e.label)
            XCTAssertEqual(r.recent, e.recent, e.label); XCTAssertEqual("\(r.recentTrend)".lowercased(), e.recentTrend, e.label)
        }
    }
}

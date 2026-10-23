import XCTest
@testable import FitLog

/// Runs tests/vectors/energy.json — the same cases the C# EnergyVectorTests run.
final class EnergyVectorTests: XCTestCase {
    private struct OutCase: Decodable { let day: Day; let kcal: Int?; let fromHealth: Bool?; let live: Bool? }
    private struct AvgCase: Decodable { let end: Day; let window: Int; let kcal: Int; let days: Int; let healthDays: Int }
    private struct BalCase: Decodable { let end: Day; let window: Int; let avgNet: Int; let totalNet: Int; let days: Int }
    private struct Case: Decodable {
        let name: String; let today: Day
        let logs: [DailyLog]; let body: [BodyMeasurement]; let sessions: [WorkoutSession]
        let caloriesIn: [String: Int]?
        let expectOut: [OutCase]?; let expectAverage: [AvgCase]?; let expectBalance: [BalCase]?
    }
    private struct File: Decodable { let cases: [Case] }

    func testSharedVectors() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "energy", withExtension: "json"))
        let file = try Wire.decoder.decode(File.self, from: Data(contentsOf: url))
        XCTAssertFalse(file.cases.isEmpty)
        for c in file.cases {
            let h = EnergyHistory(logs: c.logs, body: c.body, sessions: c.sessions, today: c.today)
            for e in c.expectOut ?? [] {
                let got = h.out(e.day)
                if let k = e.kcal {
                    XCTAssertEqual(got, EnergyOut(kcal: k, fromHealth: e.fromHealth ?? false, live: e.live ?? false), "\(c.name): out \(e.day)")
                } else {
                    XCTAssertNil(got, "\(c.name): out \(e.day)")
                }
            }
            for e in c.expectAverage ?? [] {
                let a = h.average(e.end, e.window)
                XCTAssertEqual(a?.kcal, e.kcal, c.name); XCTAssertEqual(a?.days, e.days, c.name); XCTAssertEqual(a?.healthDays, e.healthDays, c.name)
            }
            let kin = Dictionary(uniqueKeysWithValues: (c.caloriesIn ?? [:]).map { (try! Self.day($0.key), $0.value) })
            for e in c.expectBalance ?? [] {
                let b = h.balance(e.end, e.window, caloriesIn: kin)
                XCTAssertEqual(b?.avgNet, e.avgNet, c.name); XCTAssertEqual(b?.totalNet, e.totalNet, c.name); XCTAssertEqual(b?.days, e.days, c.name)
            }
        }
    }

    private static func day(_ s: String) throws -> Day {
        try JSONDecoder().decode(Day.self, from: Data("\"\(s)\"".utf8))
    }
}

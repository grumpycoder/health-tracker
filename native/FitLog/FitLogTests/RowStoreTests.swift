import XCTest
@testable import FitLog

final class RowStoreTests: XCTestCase {
    private func store() throws -> RowStore { try RowStore() }   // in-memory

    private let serverMeal: Row = [
        "id": .string("A1B2C3D4-0000-0000-0000-000000000001"),
        "time": .string("2026-10-21T09:00:00"),
        "description": .string("Frosted mini wheats"),
        "calories": .int(360),
        "fatG": .double(9.5),
        "itemsJson": .string("[{\"Name\":\"Milk\",\"Calories\":110,\"Portion\":1}]"),
        "items": .array([.object(["name": .string("Milk")])]),
        "tagList": .array([.string("High carb")]),
        "someFutureColumn": .string("keep me"),
        "isDeleted": .bool(false),
    ]

    func testEditKeepsUnmodeledFieldsAndPushStripsComputedArrays() throws {
        let s = try store()
        try s.applyPulled(["MealEntry": [serverMeal]])
        XCTAssertTrue(try s.pending().isEmpty, "pulled rows aren't dirty")

        try s.update("MealEntry", "a1b2c3d4-0000-0000-0000-000000000001", ["calories": .int(410)])
        let p = try XCTUnwrap(try s.pending().first)
        let pushed = p.row.pushable
        XCTAssertEqual(pushed["calories"], .int(410))
        XCTAssertEqual(pushed["someFutureColumn"], .string("keep me"), "unknown columns round-trip")
        XCTAssertEqual(pushed["fatG"], .double(9.5))
        XCTAssertNotNil(pushed["itemsJson"])
        XCTAssertNil(pushed["items"], "computed `items` would overwrite itemsJson on the server")
        XCTAssertNil(pushed["tagList"])
    }

    func testPullDoesNotClobberDirtyRow_andEditDuringPushStaysDirty() throws {
        let s = try store()
        let id = "a1b2c3d4-0000-0000-0000-000000000001"
        try s.applyPulled(["MealEntry": [serverMeal]])
        try s.update("MealEntry", id, ["calories": .int(410)])

        var newer = serverMeal; newer["calories"] = .int(999)
        try s.applyPulled(["MealEntry": [newer]])
        XCTAssertEqual(try s.row("MealEntry", id)?["calories"], .int(410), "local pending edit wins until pushed")

        let collected = try s.pending()
        try s.update("MealEntry", id, ["calories": .int(420)])   // edit while the push is in flight
        try s.markPushed(collected)
        XCTAssertEqual(try s.pending().count, 1, "edit made mid-push is still pending")

        try s.markPushed(try s.pending())
        XCTAssertTrue(try s.pending().isEmpty)
    }

    func testInsertAndDeleteProduceTombstone() throws {
        let s = try store()
        let id = try s.insert("DrinkEntry", ["description": .string("Water"), "ounces": .int(16)])
        try s.markPushed(try s.pending())
        try s.delete("DrinkEntry", id)
        let row = try XCTUnwrap(try s.row("DrinkEntry", id))
        XCTAssertEqual(row["isDeleted"], .bool(true))
        XCTAssertNotNil(row["deletedAt"])
        XCTAssertTrue((try s.decode("DrinkEntry") as [DrinkEntry]).isEmpty, "deleted rows are hidden")
    }

    func testIntegersStayIntegers() throws {
        let data = Data(#"{"calories":2200,"fatG":9.5,"n":null}"#.utf8)
        let row = try Row.decode(data)
        XCTAssertEqual(row["calories"], .int(2200))
        let again = String(data: try row.encoded(), encoding: .utf8)!
        XCTAssertTrue(again.contains("\"calories\":2200"), again)
    }
}

final class JSONFidelityTests: XCTestCase {
    func testZeroAndOneStayNumbers_trueFalseStayBools() throws {
        let row = try Row.decode(Data(#"{"mealType":1,"satiety":0,"isDeleted":false,"flag":true}"#.utf8))
        XCTAssertEqual(row["mealType"], .int(1))
        XCTAssertEqual(row["satiety"], .int(0))
        XCTAssertEqual(row["isDeleted"], .bool(false))
        XCTAssertEqual(row["flag"], .bool(true))
    }
}

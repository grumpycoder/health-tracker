import XCTest
@testable import FitLog

final class DraftTests: XCTestCase {
    func testItemTotalsMatchCSharp_bankersRoundingAndOneDecimal() {
        var a = MealItem(name: "Milk"); a.calories = 5; a.proteinG = 0.25; a.sodiumMg = 25
        a.portion = 0.5   // 2.5 cal → 2 (to even), 0.125 g → 0.1, 12.5 mg → 12
        let m = Macros.of([a])
        XCTAssertEqual(m.calories, 2)
        XCTAssertEqual(m.proteinG, 0.1)
        XCTAssertEqual(m.sodiumMg, 12)
        XCTAssertEqual(m.carbsG, 0, "items with no value sum to 0, like C#")
        XCTAssertTrue(Macros.of([]).isEmpty)
    }

    func testMealDraftWritesOnlyItsFields_andItemsJsonIsPascalCase() throws {
        let server: Row = ["id": .string("x"), "time": .string("2026-10-21T09:00:00"), "mealType": .int(0),
                           "description": .string("Cereal"), "tags": .string("High carb"), "calories": .int(300),
                           "satiety": .int(2)]
        var d = MealDraft(id: "x", row: server)
        XCTAssertEqual(d.mealType, .breakfast)
        XCTAssertEqual(d.satiety, .satisfied)
        var milk = MealItem(name: "Milk"); milk.calories = 110
        d.items = [d.baseItem!, milk]
        d.setTotals()
        XCTAssertEqual(d.macros.calories, 410)

        let row = d.row
        XCTAssertNil(row["tags"], "draft doesn't own tags, so the merge keeps the server's")
        XCTAssertEqual(row["time"], .string("2026-10-21T09:00:00"))
        let json = try XCTUnwrap(row.string("itemsJson"))
        XCTAssertTrue(json.contains("\"Name\":\"Milk\"") && json.contains("\"Portion\":1"), json)
        XCTAssertEqual(MealItem.parse(json).map(\.name), ["Cereal", "Milk"])
    }

    func testLibraryPrefersSavedFoods() {
        let meal: Row = ["time": .string("2026-10-21T09:00:00"), "description": .string("Bowl"),
                         "itemsJson": .string(#"[{"Name":"Milk","Calories":90,"Portion":2}]"#)]
        let saved: Row = ["name": .string("milk"), "calories": .int(110)]
        let lib = FoodLibrary(meals: [meal], drinks: [], savedFoods: [("s1", saved)])
        let it = lib.item(for: "Milk")
        XCTAssertEqual(it.calories, 110)
        XCTAssertEqual(it.portion, 1, "suggestions start at one serving")
        XCTAssertEqual(lib.suggest("mi"), ["milk"])
    }
}

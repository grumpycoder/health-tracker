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
        let json = #"{"id":"00000000-0000-0000-0000-000000000001","time":"2026-10-21T09:00:00","mealType":0,"satiety":0,"description":"Bowl","satiety":0,"itemsJson":"[{\"Name\":\"Milk\",\"Calories\":90,\"Portion\":2}]"}"#
        let meal = try! Wire.decoder.decode(MealEntry.self, from: Data(json.utf8))
        let saved: Row = ["name": .string("milk"), "calories": .int(110)]
        let lib = FoodLibrary(meals: [meal], drinks: [], savedFoods: [("s1", saved)])
        let it = lib.item(for: "Milk")
        XCTAssertEqual(it.calories, 110)
        XCTAssertEqual(it.portion, 1, "suggestions start at one serving")
        XCTAssertEqual(lib.suggest("mi"), ["milk"])
    }
}

final class ChangedFieldTests: XCTestCase {
    func testUnchangedValuesAreNotRewritten() {
        XCTAssertTrue(Store.same(.string("2026-10-23T06:53:59.4898680"), .string("2026-10-23T06:53:59"), key: "time"))
        XCTAssertFalse(Store.same(.string("2026-10-23T06:53:59"), .string("2026-10-23T07:53:59"), key: "time"))
        XCTAssertTrue(Store.same(.int(16), .double(16), key: "ounces"))
        XCTAssertFalse(Store.same(.int(32), .int(0), key: "calories"))
        XCTAssertTrue(Store.same(nil, .null, key: "portionNote"))
    }
}

final class AIParsingTests: XCTestCase {
    func testParsesFencedJsonAndLenientNumbers() {
        let obj = AIClient.parseObject("```json\n{\"calories\":\"120\",\"proteinG\":\"12g\",\"fatG\":3.5,\"sodiumMg\":null}\n```")
        XCTAssertEqual(AIClient.int(obj?["calories"]), 120)
        XCTAssertEqual(AIClient.num(obj?["proteinG"]), 12)
        XCTAssertEqual(AIClient.num(obj?["fatG"]), 3.5)
        XCTAssertNil(AIClient.int(obj?["sodiumMg"]))
    }

    func testDrinkServingsFromLabel() {
        XCTAssertEqual(AIClient.servings(drankOunces: 16, servingSize: "8 fl oz (240mL)"), 2)
        XCTAssertEqual(AIClient.servings(drankOunces: 12, servingSize: "355 mL"), 1)   // 12 ÷ 12.0
        XCTAssertEqual(AIClient.servings(drankOunces: nil, servingSize: "8 fl oz"), 1)
        XCTAssertEqual(AIClient.servings(drankOunces: 10, servingSize: "1 scoop"), 1)
    }
}

final class RepeatLastTests: XCTestCase {
    private func meal(_ json: String) -> MealEntry { try! Wire.decoder.decode(MealEntry.self, from: Data(json.utf8)) }
    private func drink(_ json: String) -> DrinkEntry { try! Wire.decoder.decode(DrinkEntry.self, from: Data(json.utf8)) }

    func testLoggedDrinksAreSuggestedByName() {
        let coke = drink(#"{"id":"00000000-0000-0000-0000-000000000002","time":"2026-10-20T12:00:00","description":"Coke Zero","ounces":12,"calories":0}"#)
        let water = drink(#"{"id":"00000000-0000-0000-0000-000000000003","time":"2026-10-20T13:00:00","description":"Water","ounces":16}"#)
        let lib = FoodLibrary(meals: [], drinks: [coke, water], savedFoods: [])
        XCTAssertEqual(lib.suggest("coke"), ["Coke Zero"])
        XCTAssertEqual(lib.suggest("wat"), ["Water"])
        XCTAssertEqual(lib.item(for: "coke zero").calories, 0)
    }

    func testRenameTouchesNameAndItemsOnlyAndKeepsOtherFields() throws {
        let row: Row = ["id": .string("m1"), "description": .string(" coke zero "), "calories": .int(0),
                        "itemsJson": .string(#"[{"Name":"Coke Zero","Calories":0,"Portion":1,"Extra":"keep"},{"Name":"Ice","Portion":2}]"#)]
        let c = try XCTUnwrap(FoodRename.changes(row, from: "Coke Zero", to: "Diet Coke"))
        XCTAssertEqual(Set(c.keys), ["description", "itemsJson"])
        XCTAssertEqual(c["description"], .string("Diet Coke"))
        let items = try JSONDecoder().decode(JSON.self, from: Data(try XCTUnwrap(c.string("itemsJson")).utf8))
        guard case .array(let a) = items, case .object(let first) = a[0], case .object(let second) = a[1] else { return XCTFail() }
        XCTAssertEqual(first["Name"], .string("Diet Coke"))
        XCTAssertEqual(first["Extra"], .string("keep"), "fields this app doesn't model survive")
        XCTAssertEqual(second["Name"], .string("Ice"))
        XCTAssertNil(FoodRename.changes(["description": .string("Water")], from: "Coke Zero", to: "Diet Coke"))
    }

    func testLookAlikeNamesAreGroupedAndSuggested() {
        let a = meal(#"{"id":"00000000-0000-0000-0000-000000000011","time":"2026-10-20T07:00:00","mealType":0,"satiety":0,"description":"Eggs, scrambled"}"#)
        let b = meal(#"{"id":"00000000-0000-0000-0000-000000000012","time":"2026-10-21T07:00:00","mealType":0,"satiety":0,"description":"scrambled eggs"}"#)
        let c = meal(#"{"id":"00000000-0000-0000-0000-000000000013","time":"2026-10-22T07:00:00","mealType":0,"satiety":0,"description":"Scrambled Eggs"}"#)
        let d = meal(#"{"id":"00000000-0000-0000-0000-000000000014","time":"2026-10-22T08:00:00","mealType":0,"satiety":0,"description":"Egg salad"}"#)
        let lib = FoodLibrary(meals: [a, b, c, d], drinks: [], savedFoods: [])
        XCTAssertEqual(lib.duplicateGroups, [["Eggs, scrambled", "Scrambled Eggs", "scrambled eggs"]])
        XCTAssertNotNil(lib.existingSpelling(of: "EGGS scrambled"))
        XCTAssertNil(lib.existingSpelling(of: "Egg salad"), "the same spelling isn't a duplicate")
        XCTAssertTrue(lib.suggest("eggs scr").contains("Eggs, scrambled"), "matches by word, ignoring the comma")
        XCTAssertFalse(lib.suggest("eggs scr").contains("Egg salad"))
    }

    func testFoodNamesAreTitleCasedWithoutTouchingDeliberateCapitals() {
        XCTAssertEqual("protein shake with whole milk".foodNameCased, "Protein Shake with Whole Milk")
        XCTAssertEqual("  coke zero ".foodNameCased, "Coke Zero")
        XCTAssertEqual("BBQ chicken".foodNameCased, "BBQ Chicken")
        XCTAssertEqual("McDonald's fries".foodNameCased, "McDonald's Fries")
        XCTAssertEqual("2% milk, 8 oz".foodNameCased, "2% Milk, 8 oz")
        XCTAssertEqual("the stir-fry".foodNameCased, "The Stir-Fry")
        XCTAssertEqual("eggs and toast".foodNameCased, "Eggs and Toast")
    }

    func testHiddenFoodsAreLeftOutOfSuggestionsOnly() {
        let coke = drink(#"{"id":"00000000-0000-0000-0000-000000000004","time":"2026-10-20T12:00:00","description":"Coke Zero","ounces":12}"#)
        var lib = FoodLibrary(meals: [], drinks: [coke], savedFoods: [])
        lib.hidden = ["coke zero"]
        XCTAssertTrue(lib.suggest("coke").isEmpty)
        XCTAssertEqual(lib.allNames, ["Coke Zero"], "still listed on the Saved foods screen")
        XCTAssertNotNil(lib.lastDrink["coke zero"], "repeat-last still works")
    }

    func testRepeatingADrinkRestoresAllItsItemsAtOneServing() {
        let old = drink(#"{"id":"00000000-0000-0000-0000-000000000001","time":"2026-10-20T07:00:00","description":"Protein shake","ounces":14,"tags":"High protein","itemsJson":"[{\"Name\":\"Whey\",\"Calories\":120,\"Portion\":1.5},{\"Name\":\"Almond milk\",\"Calories\":40,\"Portion\":2},{\"Name\":\"Banana\",\"Calories\":105,\"Portion\":1}]"}"#)
        let saved: Row = ["name": .string("Banana"), "calories": .int(110)]
        let lib = FoodLibrary(meals: [], drinks: [old], savedFoods: [("s", saved)])
        var d = DrinkDraft()
        d.repeatLast("protein shake", from: lib)
        XCTAssertEqual(d.items.map(\.name), ["Whey", "Almond milk", "Banana"])
        XCTAssertEqual(d.items.map(\.portion), [1, 1, 1], "one serving each, like the MAUI app")
        XCTAssertEqual(d.items.last?.calories, 110, "saved-food macros win")
        XCTAssertEqual(d.macros.calories, 270)
        XCTAssertEqual(d.ounces, 14)
        XCTAssertEqual(d.row["tags"], .string("High protein"))
    }

    func testRepeatingAMealCarriesNoteStarsAndTags_newestWins() {
        let older = meal(#"{"id":"00000000-0000-0000-0000-000000000002","time":"2026-10-10T08:00:00","mealType":0,"satiety":0,"description":"Oatmeal","satiety":0,"calories":200}"#)
        let newer = meal(#"{"id":"00000000-0000-0000-0000-000000000003","time":"2026-10-20T08:00:00","mealType":0,"satiety":0,"description":"Oatmeal","portionNote":"1 cup","tags":"High fiber","satiety":2,"qualityStars":4,"calories":300,"itemsJson":"[{\"Name\":\"Oats\",\"Calories\":150,\"Portion\":1},{\"Name\":\"Milk\",\"Calories\":120,\"Portion\":1}]"}"#)
        let lib = FoodLibrary(meals: [older, newer], drinks: [], savedFoods: [])
        var m = MealDraft()
        m.repeatLast("Oatmeal", from: lib)
        XCTAssertEqual(m.items.count, 2)
        XCTAssertEqual(m.macros.calories, 270)
        XCTAssertEqual(m.portionNote, "1 cup")
        XCTAssertEqual(m.stars, 4)
        XCTAssertEqual(m.row["tags"], .string("High fiber"))
    }
}

final class WidgetSnapshotTests: XCTestCase {
    func testQuickLogUpdatesCaloriesBudgetAndBars() {
        var s = WidgetSnapshot(updatedAt: Date(), day: WidgetSnapshot.today(), readiness: 70, level: "Moderate", advice: nil,
                               caloriesIn: 1000, projectedOut: 2400, avg7Net: nil,
                               bars: [.init(label: "Protein", value: 80, low: 150, high: 200, ceiling: false, unit: "g", key: "proteinG"),
                                      .init(label: "Fluids", value: 32, low: 64, high: 100, ceiling: false, unit: "oz", key: "ounces")])
        let shake = WidgetSnapshot.QuickItem(id: "drink:shake", title: "Protein shake", kind: "drink", icon: "cup.and.saucer.fill",
                                             row: Data(), macros: ["calories": 270, "proteinG": 40, "ounces": 14])
        s.apply(shake)
        XCTAssertEqual(s.caloriesIn, 1270)
        XCTAssertEqual(s.budget, 1130)
        XCTAssertEqual(s.bars.map(\.value), [120, 46])
    }
}

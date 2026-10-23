import SwiftUI

/// One day's meals and drinks, newest first, grouped by meal type, with the energy card on top.
struct DayLogView: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var auth: Auth
    @State private var day = Day.today

    var body: some View {
        NavigationStack {
            let meals = store.meals(on: day), drinks = store.drinks(on: day)
            List {
                Section {
                    EnergyCard(day: day, caloriesIn: Nutrition.calories(meals, drinks))
                }
                ForEach(MealType.allCases.filter { $0 != .drink }, id: \.self) { type in
                    let rows = meals.filter { $0.mealType == type }
                    if !rows.isEmpty {
                        Section {
                            ForEach(rows) { MealRow(meal: $0) }
                        } header: { Label(type.label, systemImage: type.symbol) }
                    }
                }
                if !drinks.isEmpty {
                    Section {
                        ForEach(drinks) { DrinkRow(drink: $0) }
                    } header: { Label("Drinks · \(Nutrition.fluidOz(drinks)) oz", systemImage: "drop") }
                }
                if meals.isEmpty && drinks.isEmpty {
                    ContentUnavailableView("Nothing logged", systemImage: "fork.knife", description: Text("No meals or drinks on this day."))
                }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button { day = day.adding(-1) } label: { Image(systemName: "chevron.left") }
                        .accessibilityLabel("Previous day")
                }
                ToolbarItem(placement: .topBarTrailing) {
                    HStack {
                        if day != .today { Button("Today") { day = .today } }
                        Button { day = day.adding(1) } label: { Image(systemName: "chevron.right") }
                            .disabled(day >= .today).accessibilityLabel("Next day")
                    }
                }
            }
            .refreshable { await store.refresh(auth: auth) }
        }
    }

    private var title: String {
        day == .today ? "Today" : day.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day())
    }
}

struct MealRow: View {
    let meal: MealEntry
    @State private var expanded = false

    var body: some View {
        let items = meal.items
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(meal.description).font(.body)
                    Text(meta).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                if let c = meal.calories {
                    Text("\(c)").font(.headline).monospacedDigit() + Text(" cal").font(.caption).foregroundColor(.secondary)
                }
            }
            if meal.calories != nil { MacroChips(p: meal.proteinG, c: meal.carbsG, f: meal.fatG) }
            if !items.isEmpty {
                DisclosureGroup(isExpanded: $expanded) {
                    ForEach(items, id: \.self) { it in
                        HStack {
                            Text(it.name)
                            if it.portion != 1 { Text("×\(it.portion.formatted())").foregroundStyle(.secondary) }
                            Spacer()
                            Text("\(it.kcal) cal").foregroundStyle(.secondary).monospacedDigit()
                        }
                        .font(.subheadline)
                    }
                } label: {
                    Text("\(items.count) item\(items.count == 1 ? "" : "s")").font(.subheadline).foregroundStyle(.tint)
                }
            }
        }
        .padding(.vertical, 2)
    }

    private var meta: String {
        var parts = [meal.time.formatted(date: .omitted, time: .shortened)]
        if let p = meal.portionNote, !p.isEmpty { parts.append(p) }
        if let s = meal.qualityStars { parts.append(String(repeating: "★", count: s)) }
        return parts.joined(separator: " · ")
    }
}

struct DrinkRow: View {
    let drink: DrinkEntry
    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 2) {
                Text(drink.description)
                Text([drink.time.formatted(date: .omitted, time: .shortened),
                      drink.ounces.map { "\($0.formatted()) oz" }].compactMap { $0 }.joined(separator: " · "))
                    .font(.caption).foregroundStyle(.secondary)
                if !drink.items.isEmpty {
                    Text(drink.items.map(\.name).joined(separator: ", ")).font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
            if let c = drink.calories {
                Text("\(c)").font(.headline).monospacedDigit() + Text(" cal").font(.caption).foregroundColor(.secondary)
            }
        }
    }
}

struct MacroChips: View {
    let p, c, f: Double?
    var body: some View {
        HStack(spacing: 6) {
            chip("P", p, .blue); chip("C", c, .orange); chip("F", f, .purple)
        }
    }
    @ViewBuilder private func chip(_ l: String, _ v: Double?, _ color: Color) -> some View {
        if let v {
            Text("\(l) \(Int(v.rounded()))g").font(.caption.weight(.medium)).monospacedDigit()
                .padding(.horizontal, 7).padding(.vertical, 2)
                .background(color.opacity(0.15), in: Capsule()).foregroundStyle(color)
        }
    }
}

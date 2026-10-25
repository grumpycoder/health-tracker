import SwiftUI

/// One day's meals and drinks grouped Breakfast → Lunch → Dinner → Snack → Drinks, each a
/// collapsible section (collapsed ones stay collapsed across days and launches).
struct DayLogView: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var auth: Auth
    @State private var day = Day.today
    @State private var editor: EditorTarget?
    @AppStorage("dayLog.collapsed") private var collapsedRaw = ""

    var body: some View {
        NavigationStack {
            let meals = store.meals(on: day), drinks = store.drinks(on: day)
            List {
                // Only the non-empty types, worked out up front: a ForEach whose rows are sometimes
                // empty (an `if` inside) lets List put sections out of order.
                let types = MealType.allCases.filter { t in t != .drink && meals.contains { $0.mealType == t } }
                ForEach(types, id: \.self) { type in
                    let rows = meals.filter { $0.mealType == type }
                    Section {
                        if !isCollapsed(type.label) {
                            ForEach(rows) { m in
                                Button { if let d = store.meal(m.id) { editor = .meal(d) } } label: { MealRow(meal: m) }
                                    .buttonStyle(.plain)
                                    .swipeActions { Button("Delete", role: .destructive) { store.deleteMeal(m.id) } }
                            }
                        }
                    } header: {
                        header(type.label, type.symbol, "\(rows.count) · \(rows.compactMap(\.calories).reduce(0, +)) cal")
                    }
                }
                if !drinks.isEmpty {
                    Section {
                        if !isCollapsed("Drinks") {
                            ForEach(drinks) { d in
                                Button { if let dd = store.drink(d.id) { editor = .drink(dd) } } label: { DrinkRow(drink: d) }
                                    .buttonStyle(.plain)
                                    .swipeActions { Button("Delete", role: .destructive) { store.deleteDrink(d.id) } }
                            }
                        }
                    } header: {
                        header("Drinks", "drop", "\(drinks.count) · \(Nutrition.fluidOz(drinks)) oz · \(drinks.compactMap(\.calories).reduce(0, +)) cal")
                    }
                }
                if meals.isEmpty && drinks.isEmpty {
                    ContentUnavailableView("Nothing logged", systemImage: "fork.knife", description: Text("No meals or drinks on this day."))
                }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                // Add sits top-left: a bottom-bar item is hidden behind the tab bar on iOS 26+.
                ToolbarItem(placement: .topBarLeading) {
                    Menu {
                        Button { editor = .meal(newMeal()) } label: { Label("Meal", systemImage: "fork.knife") }
                        Button { editor = .drink(newDrink()) } label: { Label("Drink", systemImage: "cup.and.saucer") }
                    } label: { Image(systemName: "plus") }
                    .accessibilityLabel("Add meal or drink")
                }
                ToolbarItem(placement: .topBarTrailing) {
                    HStack {
                        Button { day = day.adding(-1) } label: { Image(systemName: "chevron.left") }
                            .accessibilityLabel("Previous day")
                        if day != .today { Button("Today") { day = .today } }
                        Button { day = day.adding(1) } label: { Image(systemName: "chevron.right") }
                            .disabled(day >= .today).accessibilityLabel("Next day")
                    }
                }
            }
            .refreshable { await store.refresh(auth: auth) }
            .sheet(item: $editor) { target in
                switch target {
                case .meal(let d): MealEditor(draft: d)
                case .drink(let d): DrinkEditor(draft: d)
                }
            }
        }
    }

    private var collapsed: Set<String> { Set(collapsedRaw.split(separator: ",").map(String.init)) }
    private func isCollapsed(_ key: String) -> Bool { collapsed.contains(key) }

    /// Tappable section header: name, a summary that stays visible when collapsed, and a chevron.
    private func header(_ title: String, _ icon: String, _ summary: String) -> some View {
        Button {
            var c = collapsed
            if c.contains(title) { c.remove(title) } else { c.insert(title) }
            withAnimation { collapsedRaw = c.sorted().joined(separator: ",") }
        } label: {
            HStack {
                Label(title, systemImage: icon)
                Spacer()
                Text(summary).monospacedDigit()
                Image(systemName: "chevron.right").rotationEffect(.degrees(isCollapsed(title) ? 0 : 90))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityHint(isCollapsed(title) ? "Shows \(title.lowercased())" : "Hides \(title.lowercased())")
    }

    /// New entries default to the viewed day (at the current time of day when back-filling).
    private func onViewedDay(_ now: Date = Date()) -> Date {
        guard day != .today else { return now }
        let t = Calendar.current.dateComponents([.hour, .minute], from: now)
        return Calendar.current.date(bySettingHour: t.hour!, minute: t.minute!, second: 0, of: day.date) ?? day.date
    }
    private func newMeal() -> MealDraft { var d = MealDraft(); d.time = onViewedDay(); d.mealType = .forHour(d.time); return d }
    private func newDrink() -> DrinkDraft { var d = DrinkDraft(); d.time = onViewedDay(); return d }

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

enum EditorTarget: Identifiable {
    case meal(MealDraft), drink(DrinkDraft)
    var id: String {
        switch self {
        case .meal(let d): "m-\(d.id ?? "new")"
        case .drink(let d): "d-\(d.id ?? "new")"
        }
    }
}

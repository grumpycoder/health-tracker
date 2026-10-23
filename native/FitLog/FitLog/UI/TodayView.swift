import SwiftUI

struct TodayView: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var auth: Auth

    var body: some View {
        NavigationStack {
            let day = Day.today
            let meals = store.meals(on: day), drinks = store.drinks(on: day)
            List {
                Section {
                    EnergyCard(day: day, caloriesIn: Nutrition.calories(meals, drinks))
                } header: { Text("Energy balance") }
                Section {
                    MacroSummary(meals: meals, drinks: drinks, active: store.energy.log(day)?.isActiveDay ?? false)
                } header: { Text("Today's goals") }
            }
            .navigationTitle(Date().formatted(.dateTime.weekday(.wide).month().day()))
            .toolbar { NavigationLink { AccountView() } label: { Image(systemName: "person.crop.circle") }.accessibilityLabel("Account & sync") }
            .refreshable { await store.refresh(auth: auth) }
        }
    }
}

/// In / out / budget for the day, then one card per rolling window. Grids collapse to stacked
/// rows at accessibility text sizes so nothing gets squeezed.
struct EnergyCard: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dynamicTypeSize) private var typeSize
    let day: Day
    let caloriesIn: Int

    var body: some View {
        let energy = store.energy
        let kin = store.caloriesByDay
        let projected = day >= .today
        VStack(alignment: .leading, spacing: 14) {
            if let out = energy.out(day), caloriesIn > 0 {
                let net = caloriesIn - out.kcal
                Text(projected ? (out.live ? "Projected · Health activity so far" : "Projected full day")
                               : (out.fromHealth ? "Apple Health" : "Estimated burn"))
                    .font(.caption).foregroundStyle(.secondary).textCase(.uppercase)
                stack {
                    Stat(value: caloriesIn.formatted(), label: "In")
                    Stat(value: out.kcal.formatted(), label: projected ? "Projected out" : "Out")
                    if projected {
                        Stat(value: abs(net).formatted(), label: net > 0 ? "Over budget" : "Remaining budget", color: netColor(net))
                    } else {
                        Stat(value: signed(net), label: net > 0 ? "Surplus" : "Deficit", color: netColor(net))
                    }
                }
            }
            stack {
                WindowCard(days: 7, end: energy.window(day, 7).first!, avg: energy.average(day, 7), bal: energy.balance(day, 7, caloriesIn: kin))
                WindowCard(days: 14, end: energy.window(day, 14).first!, avg: energy.average(day, 14), bal: energy.balance(day, 14, caloriesIn: kin))
            }
            if let src = energy.average(day, 14) ?? energy.average(day, 7) {
                Text("Averages use completed days only. Out is from Apple Health on \(src.healthDays) of \(src.days) days\(src.healthDays < src.days ? ", estimated on the rest" : "").")
                    .font(.footnote).foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 6)
    }

    @ViewBuilder private func stack<C: View>(@ViewBuilder _ content: () -> C) -> some View {
        if typeSize.isAccessibilitySize { VStack(alignment: .leading, spacing: 10, content: content) }
        else { HStack(alignment: .top, spacing: 10, content: content) }
    }
}

struct Stat: View {
    let value: String
    let label: String
    var color: Color = .primary
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(value).font(.title3.weight(.semibold)).foregroundStyle(color).monospacedDigit()
            Text(label).font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct WindowCard: View {
    let days: Int
    let end: Day
    let avg: RollingAverage?
    let bal: RollingBalance?

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("\(days) days · \(range)").font(.caption).foregroundStyle(.secondary)
            if let b = bal {
                HStack(alignment: .firstTextBaseline, spacing: 2) {
                    Text(signed(b.avgNet)).font(.title3.weight(.semibold)).foregroundStyle(netColor(b.avgNet)).monospacedDigit()
                    Text("/day").font(.caption).foregroundStyle(netColor(b.avgNet))
                }
                Text(b.avgNet > 0 ? "avg surplus" : "avg deficit").font(.caption).foregroundStyle(.secondary)
                Divider()
                row("Total", "\(signed(b.totalNet)) (≈\(b.pounds > 0 ? "+" : "")\(b.pounds.formatted(.number.precision(.fractionLength(1)))) lb)")
            } else {
                Text("No food logged").font(.subheadline).foregroundStyle(.secondary)
            }
            if let a = avg { row("Avg out", "\(a.kcal.formatted())/day") }
            row("Logged", "\(bal?.days ?? 0) of \(days) days")
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.background.secondary, in: RoundedRectangle(cornerRadius: 10))
    }

    private var range: String {
        let start = end.adding(1 - days)
        let f = Date.FormatStyle().month(.abbreviated).day()
        return start.m == end.m ? "\(start.date.formatted(f))–\(end.d)" : "\(start.date.formatted(f))–\(end.date.formatted(f))"
    }

    private func row(_ label: String, _ value: String) -> some View {
        ViewThatFits(in: .horizontal) {
            HStack { Text(label).foregroundStyle(.secondary); Spacer(); Text(value).monospacedDigit() }
            VStack(alignment: .leading) { Text(label).foregroundStyle(.secondary); Text(value).monospacedDigit() }
        }
        .font(.footnote)
    }
}

/// Goal bars for the day (rest vs active ranges from the synced goals).
struct MacroSummary: View {
    @EnvironmentObject private var store: Store
    let meals: [MealEntry]
    let drinks: [DrinkEntry]
    let active: Bool

    var body: some View {
        if let g = store.goals {
            bar("Calories", Double(Nutrition.calories(meals, drinks)), "", active ? g.caloriesActiveMin : g.caloriesRestMin, active ? g.caloriesActiveMax : g.caloriesRestMax)
            bar("Protein", Nutrition.protein(meals, drinks), " g", g.proteinMin, g.proteinMax)
            bar("Carbs", Nutrition.carbs(meals, drinks), " g", active ? g.carbsActiveMin : g.carbsRestMin, active ? g.carbsActiveMax : g.carbsRestMax)
            bar("Fat", Nutrition.fat(meals, drinks), " g", g.fatMin, g.fatMax)
            bar("Fiber", Nutrition.fiber(meals, drinks), " g", g.fiberMin, g.fiberMax)
        } else {
            Text("Set targets on the Goals page to see progress.").foregroundStyle(.secondary)
        }
    }

    private func bar(_ label: String, _ value: Double, _ unit: String, _ low: Int, _ high: Int) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            ViewThatFits(in: .horizontal) {
                HStack { Text(label); Spacer(); amount(value, unit, low, high) }
                VStack(alignment: .leading) { Text(label); amount(value, unit, low, high) }
            }
            ProgressView(value: min(value, Double(high)), total: Double(max(high, 1)))
                .tint(value < Double(low) ? .blue : value > Double(high) ? .orange : .green)
        }
        .padding(.vertical, 2)
    }

    private func amount(_ v: Double, _ unit: String, _ low: Int, _ high: Int) -> some View {
        Text("\(Int(v.rounded()))\(unit) / \(low)–\(high)\(unit)").font(.subheadline).foregroundStyle(.secondary).monospacedDigit()
    }
}

func signed(_ v: Int) -> String { (v > 0 ? "+" : "") + v.formatted() }
func netColor(_ net: Int) -> Color { net > 0 ? .orange : .green }

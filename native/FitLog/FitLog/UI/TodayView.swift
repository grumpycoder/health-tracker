import SwiftUI

struct TodayView: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var auth: Auth
    @State private var editor: EditorTarget?
    @EnvironmentObject private var router: Router

    var body: some View {
        NavigationStack {
            let day = Day.today
            let meals = store.meals(on: day), drinks = store.drinks(on: day)
            List {
                Section {
                    HStack(spacing: 10) {
                        quick("Meal", "fork.knife") { var d = MealDraft(); d.time = Date(); editor = .meal(d) }
                        quick("Drink", "cup.and.saucer") { editor = .drink(DrinkDraft()) }
                        quick("+16 oz water", "drop.fill") { store.logWater(16) }
                    }
                    .listRowInsets(EdgeInsets(top: 8, leading: 12, bottom: 8, trailing: 12))
                } header: { Text("Log") }
                Section { ReadinessCard() } header: { Text("Readiness") }
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
            .onChange(of: router.addMeal, initial: true) { _, go in
                guard go else { return }
                router.addMeal = false
                var d = MealDraft(); d.time = Date(); editor = .meal(d)
            }
            .sheet(item: $editor) { t in
                switch t {
                case .meal(let d): MealEditor(draft: d)
                case .drink(let d): DrinkEditor(draft: d)
                }
            }
        }
    }
}

private func quick(_ title: String, _ icon: String, _ action: @escaping () -> Void) -> some View {
    Button(action: action) {
        VStack(spacing: 4) {
            Image(systemName: icon).font(.title3)
            // Two lines reserved on every button so a wrapped label doesn't make one taller.
            Text(title).font(.caption).multilineTextAlignment(.center).lineLimit(2, reservesSpace: true).minimumScaleFactor(0.8)
        }
        .frame(maxWidth: .infinity, minHeight: 52)
    }
    .buttonStyle(.bordered)
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
            if let max = g.addedSugarMax, max > 0 { ceiling("Added sugar", Nutrition.addedSugar(meals, drinks), " g", max) }
            ceiling("Sodium", Nutrition.sodium(meals, drinks), " mg", 2300)   // FDA daily value, same as the other apps
            if let lo = active ? g.waterActiveMin : g.waterRestMin, let hi = active ? g.waterActiveMax : g.waterRestMax, hi > 0 {
                bar("Fluids", Double(Nutrition.fluidOz(drinks)), " oz", lo, hi)
            }
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

    /// Upper-limit bar (added sugar, sodium): green while under, orange once over.
    private func ceiling(_ label: String, _ value: Double, _ unit: String, _ max: Int) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            ViewThatFits(in: .horizontal) {
                HStack { Text(label); Spacer(); limit(value, unit, max) }
                VStack(alignment: .leading) { Text(label); limit(value, unit, max) }
            }
            ProgressView(value: min(value, Double(max)), total: Double(max)).tint(value > Double(max) ? .orange : .green)
        }
        .padding(.vertical, 2)
    }

    private func limit(_ v: Double, _ unit: String, _ max: Int) -> some View {
        Text("\(Int(v.dotnetRounded))\(unit) / ≤\(max)\(unit)").font(.subheadline).foregroundStyle(.secondary).monospacedDigit()
    }

    private func amount(_ v: Double, _ unit: String, _ low: Int, _ high: Int) -> some View {
        Text("\(Int(v.rounded()))\(unit) / \(low)–\(high)\(unit)").font(.subheadline).foregroundStyle(.secondary).monospacedDigit()
    }
}

func signed(_ v: Int) -> String { (v > 0 ? "+" : "") + v.formatted() }
func netColor(_ net: Int) -> Color { net > 0 ? .orange : .green }

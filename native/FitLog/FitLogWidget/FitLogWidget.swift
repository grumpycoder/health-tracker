import AppIntents
import SwiftUI
import WidgetKit

let addMealURL = URL(string: "fitlogn://add-meal")!

struct Entry: TimelineEntry {
    let date: Date
    let snap: WidgetSnapshot?
}

struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> Entry {
        Entry(date: Date(), snap: WidgetSnapshot(updatedAt: Date(), day: WidgetSnapshot.today(), readiness: 78, level: "Moderate",
                                                  advice: "Train as planned", caloriesIn: 1240, projectedOut: 2480, avg7Net: -310))
    }
    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        completion(context.isPreview ? placeholder(in: context) : Entry(date: Date(), snap: WidgetSnapshot.load()))
    }
    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        // The app reloads timelines whenever its numbers change; this refresh also flips the
        // widget to "open Fit Log N" after midnight, when the snapshot goes stale.
        let next = min(Date().addingTimeInterval(30 * 60),
                       Calendar.current.startOfDay(for: Date()).addingTimeInterval(86_400 + 60))
        completion(Timeline(entries: [Entry(date: Date(), snap: WidgetSnapshot.load())], policy: .after(next)))
    }
}

private func readinessColor(_ s: Int?) -> Color {
    guard let s else { return .secondary }
    return s >= 80 ? .green : s >= 60 ? .yellow : s >= 40 ? .orange : .red
}

struct FitLogWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: Entry

    var body: some View {
        if let s = entry.snap, s.isToday {
            switch family {
            case .accessoryCircular: circular(s)
            case .accessoryInline: Text(inline(s))
            case .accessoryRectangular: rectangular(s)
            case .systemMedium: medium(s)
            case .systemLarge: large(s)
            default: small(s)
            }
        } else {
            VStack(spacing: 4) {
                Image(systemName: "figure.strengthtraining.traditional")
                Text("Open Fit Log N").font(.caption)
            }
        }
    }

    private func gauge(_ s: WidgetSnapshot) -> some View {
        Gauge(value: Double(s.readiness ?? 0), in: 0...100) { EmptyView() } currentValueLabel: {
            Text(s.readiness.map(String.init) ?? "—").font(.title3.bold()).monospacedDigit()
        }
        .gaugeStyle(.accessoryCircularCapacity)
        .tint(readinessColor(s.readiness))
    }

    private func budgetLine(_ s: WidgetSnapshot) -> (String, String, Color) {
        guard let b = s.budget else { return ("\(s.caloriesIn.formatted())", "cal in", .primary) }
        return b >= 0 ? (b.formatted(), "cal left", .green) : ((-b).formatted(), "cal over", .orange)
    }

    private func small(_ s: WidgetSnapshot) -> some View {
        let (v, l, c) = budgetLine(s)
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                gauge(s).frame(width: 48, height: 48)
                Text(s.level ?? "Readiness").font(.headline).lineLimit(1).minimumScaleFactor(0.7)
            }
            Spacer(minLength: 0)
            Text(v).font(.title2.bold()).foregroundStyle(c).monospacedDigit().minimumScaleFactor(0.6)
            Text(l).font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .widgetURL(addMealURL)
    }

    /// Budget (tap → Add Meal in the app) plus quick-log buttons.
    /// Budget (tap → Add Meal in the app) plus quick-log buttons.
    private func medium(_ s: WidgetSnapshot) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Link(destination: addMealURL) { budgetHeader(s) }
            quickRow(s, count: 4)
        }
    }

    /// Budget, goal bars and quick-log buttons — "where am I today" without opening the app.
    private func large(_ s: WidgetSnapshot) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Link(destination: addMealURL) { budgetHeader(s) }
            if let n = s.avg7Net {
                Text("7-day: \(n > 0 ? "+" : "")\(n.formatted())/day \(n > 0 ? "surplus" : "deficit")")
                    .font(.caption).foregroundStyle(n > 0 ? .orange : .green)
            }
            VStack(spacing: 5) { ForEach(s.bars.prefix(8)) { BarRow(bar: $0) } }
            Spacer(minLength: 0)
            quickRow(s, count: 4)
        }
    }

    private func budgetHeader(_ s: WidgetSnapshot) -> some View {
        let (v, l, c) = budgetLine(s)
        return HStack(alignment: .center, spacing: 10) {
            gauge(s).frame(width: 44, height: 44)
            VStack(alignment: .leading, spacing: 1) {
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    Text(v).font(.title2.bold()).foregroundStyle(c).monospacedDigit().minimumScaleFactor(0.6)
                    Text(l).font(.caption).foregroundStyle(.secondary)
                }
                Text("\(s.caloriesIn.formatted()) in" + (s.projectedOut.map { " · \($0.formatted()) projected out" } ?? ""))
                    .font(.caption2).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.7)
            }
            Spacer(minLength: 0)
            Image(systemName: "plus.circle.fill").font(.title3).foregroundStyle(.tint)
        }
    }

    private func quickRow(_ s: WidgetSnapshot, count: Int) -> some View {
        HStack(spacing: 6) {
            ForEach(s.quick.prefix(count)) { q in
                Button(intent: QuickLogIntent(itemID: q.id)) {
                    VStack(spacing: 2) {
                        Image(systemName: q.icon).font(.caption)
                        Text(q.id == "water" ? "+16 oz" : q.title).font(.caption2).lineLimit(2).multilineTextAlignment(.center)
                            .minimumScaleFactor(0.7)
                    }
                    .frame(maxWidth: .infinity, minHeight: 36)
                }
                .buttonStyle(.bordered)
                .tint(q.id == "water" ? .cyan : .accentColor)
            }
        }
    }

    private func circular(_ s: WidgetSnapshot) -> some View {
        Gauge(value: Double(s.readiness ?? 0), in: 0...100) { Image(systemName: "bolt.heart") } currentValueLabel: {
            Text(s.readiness.map(String.init) ?? "—").monospacedDigit()
        }
        .gaugeStyle(.accessoryCircular)
    }

    private func rectangular(_ s: WidgetSnapshot) -> some View {
        let (v, l, _) = budgetLine(s)
        return VStack(alignment: .leading) {
            Text("Readiness \(s.readiness.map(String.init) ?? "—") · \(s.level ?? "")").font(.headline).lineLimit(1)
            Text("\(v) \(l)").lineLimit(1)
        }
    }

    private func inline(_ s: WidgetSnapshot) -> String {
        let (v, l, _) = budgetLine(s)
        return "\(s.readiness.map { "⚡︎\($0) · " } ?? "")\(v) \(l)"
    }
}

@main
struct FitLogWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "FitLogToday", provider: Provider()) { entry in
            FitLogWidgetView(entry: entry).containerBackground(.fill.tertiary, for: .widget)
        }
        .configurationDisplayName("Fit Log today")
        .description("Readiness, calories left, goal bars and one-tap logging.")
        .supportedFamilies([.systemSmall, .systemMedium, .systemLarge, .accessoryCircular, .accessoryRectangular, .accessoryInline])
    }
}

struct BarRow: View {
    let bar: WidgetSnapshot.Bar
    var body: some View {
        let over = bar.value > bar.high, under = !bar.ceiling && bar.value < bar.low
        HStack(spacing: 6) {
            Text(bar.label).font(.caption2).frame(width: 64, alignment: .leading).lineLimit(1).minimumScaleFactor(0.7)
            ProgressView(value: min(bar.value, bar.high), total: max(bar.high, 1))
                .tint(over ? .orange : under ? .blue : .green)
            Text("\(Int(bar.value.rounded()))\(bar.unit == "" ? "" : " \(bar.unit)")").font(.caption2.monospacedDigit())
                .foregroundStyle(.secondary).frame(width: 58, alignment: .trailing).lineLimit(1).minimumScaleFactor(0.7)
        }
    }
}

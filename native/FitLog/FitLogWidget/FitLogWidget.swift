import SwiftUI
import WidgetKit

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
    }

    private func medium(_ s: WidgetSnapshot) -> some View {
        let (v, l, c) = budgetLine(s)
        return HStack(spacing: 14) {
            VStack(spacing: 6) {
                gauge(s).frame(width: 56, height: 56)
                Text(s.level ?? "—").font(.caption.bold())
                if let a = s.advice { Text(a).font(.caption2).foregroundStyle(.secondary).multilineTextAlignment(.center).lineLimit(2) }
            }
            .frame(width: 96)
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    Text(v).font(.title.bold()).foregroundStyle(c).monospacedDigit().minimumScaleFactor(0.6)
                    Text(l).font(.caption).foregroundStyle(.secondary)
                }
                Text("\(s.caloriesIn.formatted()) in" + (s.projectedOut.map { " · \($0.formatted()) projected out" } ?? ""))
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.7)
                if let n = s.avg7Net {
                    Text("7-day: \(n > 0 ? "+" : "")\(n.formatted())/day \(n > 0 ? "surplus" : "deficit")")
                        .font(.caption).foregroundStyle(n > 0 ? .orange : .green).lineLimit(1).minimumScaleFactor(0.7)
                }
                Spacer(minLength: 0)
                Text("Updated \(s.updatedAt.formatted(date: .omitted, time: .shortened))").font(.caption2).foregroundStyle(.tertiary)
            }
            Spacer(minLength: 0)
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
        .description("Readiness and your remaining calorie budget.")
        .supportedFamilies([.systemSmall, .systemMedium, .accessoryCircular, .accessoryRectangular, .accessoryInline])
    }
}

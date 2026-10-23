import Charts
import SwiftUI

struct SleepView: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var auth: Auth
    @State private var editing: SleepEntry?

    var body: some View {
        NavigationStack {
            List {
                if let last = store.nights.first {
                    Section { NightCard(night: last, baseline: baseline(excluding: last.day)) } header: {
                        Text(last.day == .today ? "Last night" : last.day.date.formatted(.dateTime.weekday(.wide).month().day()))
                    }
                } else {
                    Section { Text("No sleep from Apple Health yet. Wear your Watch to bed, or check Health access for Sleep.").foregroundStyle(.secondary) }
                }
                if store.nights.count >= 3 { Section("Trends") { SleepTrends(nights: store.nights) } }
                Section("Nights") {
                    ForEach(store.nights.dropFirst()) { n in
                        NavigationLink { List { NightCard(night: n, baseline: baseline(excluding: n.day)) }.navigationTitle(n.day.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day())) } label: {
                            HStack {
                                Text(n.day.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day()))
                                Spacer()
                                Text(hm(n.asleep)).monospacedDigit()
                                Text(n.hasStages ? "\(n.estimatedScore)" : "—").foregroundStyle(.secondary).monospacedDigit().frame(width: 32, alignment: .trailing)
                            }
                        }
                    }
                }
                let manual = store.sleepEntries.prefix(30)
                if !manual.isEmpty {
                    Section {
                        ForEach(Array(manual)) { e in
                            Button { editing = e } label: {
                                HStack {
                                    Text(e.date.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day())).foregroundStyle(.primary)
                                    Spacer()
                                    Text(e.durationHours.map { "\(GoalProgress.f1($0)) h" } ?? "—").monospacedDigit()
                                    Text(e.sleepScore.map { "\($0)\(e.scoreEstimated == true ? " est." : "")" } ?? "—").foregroundStyle(.secondary).monospacedDigit()
                                }
                            }
                            .swipeActions { Button("Delete", role: .destructive) { store.deleteSleep(e.id) } }
                        }
                    } header: { Text("Sleep log (synced)") } footer: {
                        Text("Imported automatically from Health — tap to enter Apple's real Sleep Score or a note.")
                    }
                }
            }
            .navigationTitle("Sleep")
            .refreshable { await store.refresh(auth: auth) }
            .sheet(item: $editing) { SleepEditor(entry: $0) }
        }
    }

    /// 14-night averages (excluding the night shown) for vitals comparisons.
    private func baseline(excluding day: Day) -> SleepNight.Vitals {
        let ns = store.nights.filter { $0.day != day }.prefix(14)
        func avg(_ f: (SleepNight.Vitals) -> Double?) -> Double? {
            let xs = ns.compactMap { f($0.vitals) }; return xs.isEmpty ? nil : xs.reduce(0, +) / Double(xs.count)
        }
        return .init(hrvMs: avg(\.hrvMs), restingHR: avg(\.restingHR), respiratoryRate: avg(\.respiratoryRate),
                     wristTempC: avg(\.wristTempC), spo2: avg(\.spo2))
    }
}

struct NightCard: View {
    let night: SleepNight
    let baseline: SleepNight.Vitals
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text(hm(night.asleep)).font(.largeTitle.bold()).monospacedDigit()
                Text("asleep").foregroundStyle(.secondary)
                Spacer()
                if night.hasStages {
                    VStack(alignment: .trailing) {
                        Text("\(night.estimatedScore)").font(.title2.bold()).monospacedDigit()
                        Text("score (est.)").font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            Text("\(night.bedtime.formatted(date: .omitted, time: .shortened)) – \(night.wake.formatted(date: .omitted, time: .shortened))"
                 + (night.efficiency.map { " · \(Int(($0 * 100).dotnetRounded))% efficient" } ?? "")
                 + " · \(night.awakenings) awakening\(night.awakenings == 1 ? "" : "s")")
                .font(.subheadline).foregroundStyle(.secondary)
            if night.hasStages {
                Hypnogram(night: night).frame(height: 110)
                stack {
                    stageStat(.deep, night.deep); stageStat(.rem, night.rem); stageStat(.core, night.core); stageStat(.awake, night.awake)
                }
            }
            let v = night.vitals
            if v.hrvMs != nil || v.restingHR != nil || v.respiratoryRate != nil || v.wristTempC != nil || v.spo2 != nil {
                Divider()
                Text("Overnight").font(.caption).foregroundStyle(.secondary).textCase(.uppercase)
                LazyVGrid(columns: [GridItem(.adaptive(minimum: typeSize.isAccessibilitySize ? 260 : 140), alignment: .leading)], alignment: .leading, spacing: 10) {
                    vital("HRV", v.hrvMs, baseline.hrvMs, "ms", higherIsBetter: true)
                    vital("Resting HR", v.restingHR, baseline.restingHR, "bpm", higherIsBetter: false)
                    vital("Respiratory", v.respiratoryRate, baseline.respiratoryRate, "br/min", higherIsBetter: nil)
                    vital("Blood oxygen", v.spo2, baseline.spo2, "%", higherIsBetter: true)
                    if let t = v.wristTempC, let b = baseline.wristTempC {
                        VitalTile(label: "Wrist temp", value: String(format: "%+.1f °C", t - b), note: "vs your 14-night avg", color: abs(t - b) >= 0.5 ? .orange : .secondary)
                    }
                }
            }
            Text("From \(sourceName) · Apple's own Sleep Score isn't shared with apps, so this score is Fit Log's estimate.")
                .font(.caption2).foregroundStyle(.tertiary)
        }
        .padding(.vertical, 6)
    }

    private var sourceName: String { night.source.contains("com.apple.health") ? "Apple Watch" : night.source }

    @ViewBuilder private func stack<C: View>(@ViewBuilder _ c: () -> C) -> some View {
        if typeSize.isAccessibilitySize { VStack(alignment: .leading, spacing: 8, content: c) } else { HStack(spacing: 8, content: c) }
    }

    private func stageStat(_ s: SleepNight.Stage, _ t: TimeInterval) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(hm(t)).font(.headline).monospacedDigit()
            Label(s.label, systemImage: "circle.fill").font(.caption).foregroundStyle(stageColor(s)).labelStyle(.titleAndIcon)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder private func vital(_ label: String, _ v: Double?, _ base: Double?, _ unit: String, higherIsBetter: Bool?) -> some View {
        if let v {
            let delta = base.map { v - $0 }
            let good = delta.flatMap { d in higherIsBetter.map { $0 ? d > 0 : d < 0 } }
            VitalTile(label: label, value: "\(GoalProgress.f1(v)) \(unit)",
                      note: delta.map { "\($0 >= 0 ? "▲" : "▼") \(GoalProgress.f1(abs($0))) vs avg" } ?? "",
                      color: good == nil ? .secondary : good! ? .green : .orange)
        }
    }
}

struct VitalTile: View {
    let label, value, note: String
    let color: Color
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label).font(.caption).foregroundStyle(.secondary)
            Text(value).font(.headline).monospacedDigit()
            if !note.isEmpty { Text(note).font(.caption2).foregroundStyle(color) }
        }
    }
}

/// Stage timeline for one night (Awake on top, Deep at the bottom, like Apple's chart).
struct Hypnogram: View {
    let night: SleepNight
    var body: some View {
        Chart(night.segments.filter { $0.stage != .unspecified }) { s in
            RectangleMark(xStart: .value("Start", s.start), xEnd: .value("End", s.end), y: .value("Stage", s.stage.label))
                .foregroundStyle(stageColor(s.stage))
        }
        .chartYScale(domain: ["Awake", "REM", "Core", "Deep"])
        .chartXAxis { AxisMarks(values: .stride(by: .hour, count: 2)) { AxisValueLabel(format: .dateTime.hour()) } }
    }
}

struct SleepTrends: View {
    let nights: [SleepNight]
    var body: some View {
        let w7 = Array(nights.prefix(7)), w14 = Array(nights.prefix(14))
        Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 6) {
            GridRow { Text(""); Text("7 nights").font(.caption).foregroundStyle(.secondary); Text("14 nights").font(.caption).foregroundStyle(.secondary) }
            GridRow { Text("Asleep"); Text(hm(avg(w7, \.asleep))); Text(hm(avg(w14, \.asleep))) }
            GridRow { Text("Deep + REM"); Text(hm(avg(w7) { $0.deep + $0.rem })); Text(hm(avg(w14) { $0.deep + $0.rem })) }
            GridRow { Text("Bedtime"); Text(bedtime(w7)); Text(bedtime(w14)) }
            GridRow { Text("Consistency"); Text(spread(w7)); Text(spread(w14)) }
        }
        .monospacedDigit()
        Text("Consistency = how much your bedtime varies (±). Under ±30 min is steady.").font(.caption2).foregroundStyle(.secondary)
    }

    private func avg(_ ns: [SleepNight], _ f: (SleepNight) -> TimeInterval) -> TimeInterval { ns.isEmpty ? 0 : ns.map(f).reduce(0, +) / Double(ns.count) }
    private func avg(_ ns: [SleepNight], _ k: KeyPath<SleepNight, TimeInterval>) -> TimeInterval { avg(ns) { $0[keyPath: k] } }

    // Bedtime as minutes from 6 PM so midnight-crossing averages work.
    private func minutes(_ n: SleepNight) -> Double {
        let c = Calendar.current.dateComponents([.hour, .minute], from: n.bedtime)
        return Double(((c.hour! + 24 - 18) % 24) * 60 + c.minute!)
    }
    private func bedtime(_ ns: [SleepNight]) -> String {
        guard !ns.isEmpty else { return "—" }
        let m = Int(ns.map(minutes).reduce(0, +) / Double(ns.count)) + 18 * 60
        let d = Calendar.current.date(bySettingHour: (m / 60) % 24, minute: m % 60, second: 0, of: Date())!
        return d.formatted(date: .omitted, time: .shortened)
    }
    private func spread(_ ns: [SleepNight]) -> String {
        guard ns.count >= 2 else { return "—" }
        let xs = ns.map(minutes), mean = xs.reduce(0, +) / Double(xs.count)
        let sd = (xs.map { ($0 - mean) * ($0 - mean) }.reduce(0, +) / Double(xs.count)).squareRoot()
        return "±\(Int(sd.dotnetRounded)) min"
    }
}

struct SleepEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let entry: SleepEntry
    @State private var hours: Double?
    @State private var score: Int?
    @State private var interruptions: Int?
    @State private var notes = ""

    var body: some View {
        NavigationStack {
            Form {
                OptionalNumberField("Hours asleep", value: $hours)
                OptionalIntField("Sleep score (0–100)", value: $score)
                OptionalIntField("Interruptions", value: $interruptions)
                TextField("Notes", text: $notes, axis: .vertical)
                if entry.scoreEstimated == true { Text("The score is Fit Log's estimate. Enter Apple's Sleep Score to replace it.").font(.footnote).foregroundStyle(.secondary) }
            }
            .navigationTitle(entry.date.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day()))
            .navigationBarTitleDisplayMode(.inline)
            .onAppear { hours = entry.durationHours; score = entry.sleepScore; interruptions = entry.interruptions; notes = entry.notes ?? "" }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        store.saveSleep(entry.id, hours: hours.map { max(0, $0) }, score: score.map { min(100, max(0, $0)) },
                                        interruptions: interruptions.map { max(0, $0) }, notes: notes)
                        dismiss()
                    }
                }
            }
        }
        .presentationDetents([.medium])
    }
}

func hm(_ t: TimeInterval) -> String {
    let m = Int((t / 60).dotnetRounded)
    return "\(m / 60)h \(String(format: "%02d", m % 60))m"
}

func stageColor(_ s: SleepNight.Stage) -> Color {
    switch s { case .awake: .orange; case .rem: .cyan; case .core: .blue; case .deep: .indigo; case .unspecified: .gray }
}

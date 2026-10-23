import Charts
import SwiftUI

struct BodyView: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var auth: Auth
    @State private var editing: MeasurementTarget?
    @State private var pullMessage: String?
    @State private var pulling = false

    private var history: [BodyMeasurement] { store.body.sorted { $0.date > $1.date } }

    var body: some View {
        NavigationStack {
            List {
                let rows = GoalProgress.overview(store.goals, store.body.sorted { $0.date < $1.date }).filter { $0.goal != nil }
                if !rows.isEmpty {
                    Section("Goal progress") { ForEach(rows) { GoalRowView(row: $0) } }
                }
                let weights = store.body.compactMap { m in m.weightLbs.map { (m.date.date, $0) } }.sorted { $0.0 < $1.0 }
                if weights.count >= 2 {
                    Section("Weight") {
                        Chart(weights, id: \.0) { d, w in
                            LineMark(x: .value("Date", d), y: .value("lb", w)).interpolationMethod(.monotone)
                            PointMark(x: .value("Date", d), y: .value("lb", w)).symbolSize(12)
                            if let g = store.goals?.goalWeightLbs { RuleMark(y: .value("Goal", g)).foregroundStyle(.green.opacity(0.6)).lineStyle(.init(dash: [4])) }
                        }
                        .chartYScale(domain: .automatic(includesZero: false))
                        .frame(height: 180)
                    }
                }
                Section {
                    Button { editing = .new } label: { Label("Add measurement", systemImage: "plus.circle.fill") }
                    if store.health.isAvailable {
                        Button {
                            pulling = true
                            Task { pullMessage = await store.pullBodyFromHealth(); pulling = false }
                        } label: { Label(pulling ? "Pulling…" : "Pull from Apple Health", systemImage: "heart") }
                            .disabled(pulling)
                        if let m = pullMessage { Text(m).font(.footnote).foregroundStyle(.secondary) }
                    }
                }
                Section("History") {
                    if history.isEmpty { Text("No measurements yet.").foregroundStyle(.secondary) }
                    ForEach(history, id: \.id) { m in
                        Button { if let id = m.id { editing = .edit(id) } } label: { MeasurementRow(m: m) }
                            .buttonStyle(.plain)
                            .swipeActions { if let id = m.id { Button("Delete", role: .destructive) { store.deleteMeasurement(id) } } }
                    }
                }
            }
            .navigationTitle("Body")
            .refreshable { await store.refresh(auth: auth) }
            .sheet(item: $editing) { t in MeasurementEditor(target: t) }
        }
    }
}

enum MeasurementTarget: Identifiable {
    case new, edit(UUID)
    var id: String { if case .edit(let u) = self { u.uuidString } else { "new" } }
}

struct GoalRowView: View {
    let row: GoalProgress.Row
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Circle().fill(color(row.trend)).frame(width: 9, height: 9)
                Text(row.label).font(.headline)
                if !row.arrow.isEmpty { Text(row.arrow).foregroundStyle(.secondary) }
                Spacer()
                Text(values).font(.subheadline).monospacedDigit().foregroundStyle(.secondary)
            }
            ProgressView(value: Double(row.percent ?? 0), total: 100).tint(color(row.trend))
            HStack {
                Text(row.met ? "Goal met ✓" : row.status).font(.caption).foregroundStyle(row.met ? .green : .secondary)
                if !row.achieved.isEmpty { Text("· \(row.achieved)").font(.caption).foregroundStyle(.secondary) }
                Spacer()
                if !row.recent.isEmpty { Text(row.recent).font(.caption).foregroundStyle(color(row.recentTrend)) }
            }
        }
        .padding(.vertical, 2)
    }

    private var values: String {
        let f = GoalProgress.f1
        let start = (row.baseline != nil && row.baseline != row.current) ? "\(f(row.baseline!)) → " : ""
        return "\(start)\(row.current.map(f) ?? "—") → \(row.goal.map(f) ?? "—") \(row.unit)"
    }

    private func color(_ t: GoalProgress.Trend) -> Color {
        switch t { case .met: .green; case .toward: .green; case .away: .orange; case .flat: .gray; case .noData: .secondary }
    }
}

struct MeasurementRow: View {
    let m: BodyMeasurement
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(m.date.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day().year()))
                Spacer()
                if let w = m.weightLbs { Text("\(GoalProgress.f1(w)) lb").font(.headline).monospacedDigit() }
            }
            let bits = [m.waistInches.map { "waist \(GoalProgress.f1($0))" }, m.bodyFatPercent.map { "fat \(GoalProgress.f1($0))%" },
                        m.basalMetabolicRate.map { "BMR \($0)" }].compactMap { $0 }
            if !bits.isEmpty { Text(bits.joined(separator: " · ")).font(.caption).foregroundStyle(.secondary) }
        }
    }
}

/// Add or edit a measurement. Every field is optional; at least one is required; none may be negative.
struct MeasurementEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let target: MeasurementTarget
    @State private var date = Date()
    @State private var v: [String: Double] = [:]
    @State private var bmr: Int?
    @State private var metabolicAge: Int?
    @State private var notes = ""
    @State private var warn: String?
    @State private var loaded = false

    private static let tape: [(String, String)] = [("waistInches", "Waist"), ("chestInches", "Chest"), ("hipsInches", "Hips"),
        ("armsInches", "Arms"), ("forearmsInches", "Forearms"), ("thighsInches", "Thighs"), ("shouldersInches", "Shoulders"), ("calvesInches", "Calves")]
    private static let hume: [(String, String)] = [("bodyFatPercent", "Body fat (%)"), ("muscleMassLbs", "Muscle mass (lb)"),
        ("visceralFat", "Visceral fat"), ("bodyWaterPercent", "Body water (%)")]

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    DatePicker("Date", selection: $date, displayedComponents: .date)
                    field("weightLbs", "Weight (lb)")
                }
                Section("Tape (inches)") { ForEach(Self.tape, id: \.0) { field($0.0, $0.1) } }
                Section("Body composition (Hume)") {
                    ForEach(Self.hume, id: \.0) { field($0.0, $0.1) }
                    OptionalIntField("BMR (kcal)", value: $bmr)
                    OptionalIntField("Metabolic age", value: $metabolicAge)
                }
                Section { TextField("Clothing fit notes", text: $notes, axis: .vertical) }
                if let w = warn { Section { Text(w).foregroundStyle(.red) } }
            }
            .navigationTitle(isNew ? "Add measurement" : "Edit measurement")
            .navigationBarTitleDisplayMode(.inline)
            .onAppear(perform: load)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button("Save", action: save) }
            }
        }
    }

    private var isNew: Bool { if case .new = target { true } else { false } }

    private func field(_ key: String, _ label: String) -> some View {
        OptionalNumberField(label, value: Binding(get: { v[key] }, set: { v[key] = $0 }))
    }

    private func load() {
        guard !loaded else { return }
        loaded = true
        guard case .edit(let id) = target, let r = store.measurementRow(id) else { return }
        if let d = r.string("date"), let day = try? JSONDecoder().decode(Day.self, from: Data("\"\(d)\"".utf8)) { date = day.date }
        for k in ["weightLbs"] + Self.tape.map(\.0) + Self.hume.map(\.0) { v[k] = r.double(k) }
        bmr = r.int("basalMetabolicRate"); metabolicAge = r.int("metabolicAge"); notes = r.string("clothingFitNotes") ?? ""
    }

    private func save() {
        let n = notes.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !v.values.isEmpty || bmr != nil || metabolicAge != nil || !n.isEmpty else { warn = "Enter at least one measurement."; return }
        guard v.values.allSatisfy({ $0 >= 0 }), (bmr ?? 0) >= 0, (metabolicAge ?? 0) >= 0 else { warn = "Measurement values cannot be negative."; return }
        let day = Day(date)
        var fields: Row = ["date": Wire.day(day), "basalMetabolicRate": .i(bmr), "metabolicAge": .i(metabolicAge),
                           "clothingFitNotes": .s(n.isEmpty ? nil : n)]
        for k in ["weightLbs"] + Self.tape.map(\.0) + Self.hume.map(\.0) { fields[k] = .d(v[k]) }
        var id: UUID?
        if case .edit(let u) = target { id = u }
        store.saveMeasurement(id: id, day: day, fields: fields)
        dismiss()
    }
}

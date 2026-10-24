import SwiftUI

/// Why a day's readiness is what it is: each factor's measurement, baseline, score, weight and
/// contribution, the nights and samples behind it, and anything missing — all from the stored
/// snapshot, so a past day shows exactly what was calculated then.
struct ReadinessDetailView: View {
    @EnvironmentObject private var store: Store
    let snapshot: ReadinessSnapshot
    private var r: Readiness { snapshot.result }

    var body: some View {
        List {
            Section {
                HStack(alignment: .firstTextBaseline) {
                    Text("\(r.score)").font(.largeTitle.bold()).monospacedDigit().foregroundStyle(readinessColor(r.score))
                    Text(r.label).font(.headline)
                }
                Text(classification).font(.subheadline).foregroundStyle(.secondary)
            } header: { Text(snapshot.day.date.formatted(.dateTime.weekday(.wide).month().day())) }

            Section {
                ForEach(r.factors) { FactorDetail(factor: $0) }
                LabeledContent("Weighted total") { Text(String(format: "%.1f → %d", r.rawScore, Int(r.rawScore.dotnetRounded))).monospacedDigit() }
                if !r.flags.isEmpty { LabeledContent("Illness cap") { Text("40").monospacedDigit() } }
            } header: { Text("Factors") } footer: {
                Text("Heart signals score 75 when at your norm and move 20 points per standard deviation. Weights are shared out over the factors present, so a missing one doesn't count as zero.")
            }

            if !r.issues.isEmpty || !r.flags.isEmpty || r.calibrating {
                Section("Missing or unreliable data") {
                    ForEach(r.flags, id: \.self) { Label($0, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.orange) }
                    ForEach(r.issues, id: \.self) { Label($0, systemImage: "exclamationmark.circle") }
                    if r.calibrating { Label("Calibrating: fewer than \(Readiness.minBaselineDays) nights in a baseline, so comparisons are rough.", systemImage: "hourglass") }
                }
            }

            Section {
                ForEach(snapshot.inputs.nights.prefix(3), id: \.day) { NightRow(night: $0, today: snapshot.day) }
            } header: { Text("Recent nights") } footer: {
                Text("Overnight HRV and sleeping HR use only readings taken while asleep\(snapshot.inputs.vitalsSource.map { " from your \($0)" } ?? ""). Last night's HRV here is the same value the Sleep tab shows.")
            }

            Section("Calculation") {
                LabeledContent("Algorithm", value: "v\(snapshot.algorithmVersion)")
                LabeledContent("Calculated", value: snapshot.computedAt.formatted(date: .abbreviated, time: .shortened))
                if snapshot.revisions > 1 {
                    LabeledContent("First calculated", value: snapshot.firstComputedAt.formatted(date: .omitted, time: .shortened))
                    LabeledContent("Updated", value: "\(snapshot.revisions - 1)× as Health data arrived")
                }
                LabeledContent("Nights of data", value: "\(snapshot.inputs.nights.count)")
                NavigationLink("Past days") { ReadinessHistoryView() }
            }
        }
        .navigationTitle("Readiness details")
        .navigationBarTitleDisplayMode(.inline)
    }

    private var classification: String {
        let band = switch r.score { case 80...: "80 or more"; case 60..<80: "60–79"; case 40..<60: "40–59"; default: "under 40" }
        let cap = r.flags.isEmpty ? "" : " The score was capped at 40 because of a possible-illness signal."
        return "\(r.level) is \(band) (Ready 80+, Moderate 60–79, Low 40–59, Rest under 40).\(cap)"
    }
}

private struct FactorDetail: View {
    let factor: Readiness.Factor

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(factor.name).font(.headline)
                Spacer()
                Text("\(factor.score)").font(.headline.monospacedDigit()).foregroundStyle(readinessFactorColor(factor.score))
            }
            Text(factor.detail).font(.subheadline).foregroundStyle(.secondary)
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 3) {
                ForEach(rows, id: \.0) { k, v in
                    GridRow { Text(k).foregroundStyle(.secondary); Text(v).monospacedDigit() }
                }
            }
            .font(.caption)
        }
        .padding(.vertical, 2)
    }

    private func num(_ v: Double?) -> String { v.map { GoalProgress.f1($0) } ?? "—" }

    private var rows: [(String, String)] {
        var out: [(String, String)] = []
        let u = factor.unit
        switch factor.name {
        case "Training load":
            out.append(("Last 3 days", "\(num(factor.value)) \(u)"))
            out.append(("Usual 3 days", "\(num(factor.baseline)) \(u)"))
            if let r = factor.ratio { out.append(("Ratio", String(format: "%.2f×", r))) }
        case "Sleep":
            out.append(("Asleep", "\(num(factor.value)) \(u)"))
            out.append(("Target", "\(num(factor.baseline)) \(u) (70 pts) + stages 15 + efficiency 15"))
        default:
            out.append(("Current", "\(num(factor.value)) \(u)" + (factor.nights.count > 1 ? " (\(factor.nights.count)-night avg)" : " (last night)")))
            out.append(("Your norm", "\(num(factor.baseline)) \(u) · median of \(factor.baselineN ?? 0) nights"))
            if let z = factor.z { out.append(("Difference", String(format: "%+.2f SD", z))) }
            if let n = factor.samples { out.append(("Readings", "\(n) while asleep")) }
        }
        out.append(("Weight", String(format: "%.0f%% (%.0f%% of today's score)", factor.weight * 100, factor.share * 100)))
        out.append(("Contribution", String(format: "%.1f points", factor.contribution)))
        return out
    }
}

private struct NightRow: View {
    let night: NightMetrics
    let today: Day

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(night.day == today ? "Last night" : night.day.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day()))
                .font(.subheadline.weight(.medium))
            Text("\(hm(night.sleepHours * 3600)) asleep" + (night.efficiency.map { " · \(Int(($0 * 100).dotnetRounded))% efficient" } ?? ""))
            Text(vitals)
        }
        .font(.caption)
        .foregroundStyle(.secondary)
    }

    private var vitals: String {
        let hrv: String = night.hrvMs.map { "\(GoalProgress.f1($0)) ms" } ?? "—"
        let hr: String = night.sleepingHR.map { "\(GoalProgress.f1($0)) bpm" } ?? "—"
        let s = night.hrvSamples == 1 ? "" : "s"
        return "HRV \(hrv) (\(night.hrvSamples) reading\(s)) · HR \(hr) (\(night.hrSamples))"
    }
}

/// Stored daily calculations, newest first, with a JSON export for review.
struct ReadinessHistoryView: View {
    @EnvironmentObject private var store: Store

    var body: some View {
        List {
            if store.readinessHistory.isEmpty {
                Text("No stored calculations yet.").foregroundStyle(.secondary)
            }
            ForEach(store.readinessHistory) { s in
                NavigationLink { ReadinessDetailView(snapshot: s) } label: {
                    HStack {
                        Text(s.day.date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day()))
                        if s.algorithmVersion != Readiness.algorithmVersion { Text("v\(s.algorithmVersion)").font(.caption).foregroundStyle(.secondary) }
                        Spacer()
                        Text("\(s.result.score)").font(.headline.monospacedDigit()).foregroundStyle(readinessColor(s.result.score))
                        Text(s.result.level).foregroundStyle(.secondary).frame(width: 80, alignment: .leading)
                    }
                }
            }
        }
        .navigationTitle("Readiness history")
        .toolbar {
            if !store.readinessHistory.isEmpty {
                ShareLink(item: exportJSON, preview: SharePreview("Readiness snapshots")) { Image(systemName: "square.and.arrow.up") }
            }
        }
    }

    private var exportJSON: String {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        e.dateEncodingStrategy = .iso8601
        return (try? e.encode(store.readinessHistory)).flatMap { String(data: $0, encoding: .utf8) } ?? "[]"
    }
}

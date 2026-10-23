import SwiftUI

/// Today's readiness: the score, what drove it, and the (optional) check-in.
struct ReadinessCard: View {
    @EnvironmentObject private var store: Store
    @State private var checkIn = false

    var body: some View {
        if let r = store.readiness {
            VStack(alignment: .leading, spacing: 10) {
                HStack(alignment: .center, spacing: 14) {
                    Gauge(value: Double(r.score), in: 0...100) { EmptyView() } currentValueLabel: {
                        Text("\(r.score)").font(.title2.bold()).monospacedDigit()
                    }
                    .gaugeStyle(.accessoryCircularCapacity)
                    .tint(color(r.score))
                    .scaleEffect(1.25)
                    .frame(width: 70, height: 70)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(r.label).font(.headline)
                        if r.calibrating { Text("Calibrating — baselines need ~2 weeks of Watch data").font(.caption).foregroundStyle(.secondary) }
                        if !r.missing.isEmpty { Text("No \(r.missing.joined(separator: " or ")) data in Health — scored from the rest").font(.caption).foregroundStyle(.secondary) }
                    }
                }
                ForEach(r.flags, id: \.self) { f in
                    Label(f, systemImage: "exclamationmark.triangle.fill").font(.subheadline).foregroundStyle(.orange)
                }
                ForEach(r.factors) { f in
                    HStack {
                        Text(f.name).font(.subheadline)
                        Spacer()
                        Text(f.detail).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.trailing)
                        Text("\(f.score)").font(.subheadline.monospacedDigit().weight(.semibold)).foregroundStyle(color(f.score)).frame(width: 34, alignment: .trailing)
                    }
                }
                Divider()
                let entry = store.todaysRecovery
                Button { checkIn = true } label: {
                    HStack {
                        Text(entry == nil || entry!.isAuto ? "How do you feel? (optional check-in)" : "Checked in · recovery \(entry!.recoveryRating ?? 0)/10")
                        Spacer()
                        Image(systemName: "chevron.right").font(.caption).foregroundStyle(.tertiary)
                    }
                }
                Text("Compared to your own recent norms. A training guide, not medical advice.").font(.caption2).foregroundStyle(.tertiary)
            }
            .padding(.vertical, 4)
            .sheet(isPresented: $checkIn) { CheckInSheet(readiness: r) }
        } else {
            Text("Readiness appears once Apple Health has HRV, resting heart rate or sleep data.").foregroundStyle(.secondary)
        }
    }

    private func color(_ s: Int) -> Color { s >= 80 ? .green : s >= 60 ? .yellow : s >= 40 ? .orange : .red }
}

/// Recovery/fatigue pre-filled from readiness; soreness and notes are yours. Saving makes the
/// day's entry your own (the auto-fill won't touch it again).
struct CheckInSheet: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let readiness: Readiness
    @State private var recovery = 5
    @State private var fatigue = 5
    @State private var soreness: Set<String> = []
    @State private var severity = 0
    @State private var notes = ""

    static let locations = ["Lower back", "Mid-back", "Thighs", "Shoulders", "Core", "Other"]   // same as the MAUI app
    static let severities = ["None", "Mild", "Moderate", "Severe"]

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Stepper(value: $recovery, in: 1...10) { LabeledContent("Recovery", value: "\(recovery)/10") }
                    Stepper(value: $fatigue, in: 1...10) { LabeledContent("Fatigue", value: "\(fatigue)/10") }
                } footer: { Text("Pre-filled from readiness \(readiness.score). Adjust if it doesn't match how you feel.") }
                Section("Soreness") {
                    ForEach(Self.locations, id: \.self) { l in
                        Button { if soreness.contains(l) { soreness.remove(l) } else { soreness.insert(l) } } label: {
                            HStack { Text(l).foregroundStyle(.primary); Spacer(); if soreness.contains(l) { Image(systemName: "checkmark").foregroundStyle(.tint) } }
                        }
                    }
                    Picker("Severity", selection: $severity) { ForEach(0..<4) { Text(Self.severities[$0]).tag($0) } }
                }
                Section { TextField("Notes", text: $notes, axis: .vertical) }
            }
            .navigationTitle("Check-in")
            .navigationBarTitleDisplayMode(.inline)
            .onAppear {
                if let e = store.todaysRecovery, !e.isAuto {
                    recovery = e.recoveryRating ?? readiness.recoveryRating; fatigue = e.fatigueRating ?? readiness.fatigueRating
                    soreness = Set((e.sorenessLocations ?? "").split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) })
                    severity = e.sorenessSeverity ?? 0; notes = e.notes ?? ""
                } else {
                    recovery = readiness.recoveryRating; fatigue = readiness.fatigueRating
                }
            }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        let ordered = Self.locations.filter(soreness.contains)
                        store.saveCheckIn(day: .today, recovery: recovery, fatigue: fatigue, soreness: ordered, severity: severity, notes: notes)
                        dismiss()
                    }
                }
            }
        }
    }
}

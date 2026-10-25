import SwiftUI

/// Account, goals and settings — reached from the Today toolbar.
struct ProfileView: View {
    @EnvironmentObject private var auth: Auth
    @EnvironmentObject private var store: Store
    @Environment(\.openURL) private var openURL

    var body: some View {
        List {
            Section("Goals") {
                NavigationLink { GoalsView() } label: { Label("Goals & targets", systemImage: "target") }
            }
            Section {
                NavigationLink { RemindersView() } label: { Label("Reminders & notifications", systemImage: "bell") }
                NavigationLink { MedsView() } label: { Label("Medications", systemImage: "pills") }
                NavigationLink { SavedFoodsView() } label: { Label("Saved foods", systemImage: "books.vertical") }
                NavigationLink { AIPrivacyView() } label: { Label("What's sent to the AI", systemImage: "hand.raised") }
                Button { if let u = URL(string: UIApplication.openSettingsURLString) { openURL(u) } } label: {
                    Label("iPhone settings for Fit Log N", systemImage: "gear")
                }
            } header: { Text("Settings") } footer: {
                Text("Text size follows your iPhone's Display & Text Size setting. Apple Health access is under Health → Sharing → Apps.")
            }
            Section("Sync") {
                LabeledContent("Last synced", value: store.lastSync?.formatted(date: .abbreviated, time: .shortened) ?? "—")
                if let r = store.sync.lastResult { LabeledContent("Last sync", value: r) }
                Button(store.syncing ? "Syncing…" : "Sync now") { Task { await store.refresh(auth: auth) } }
                    .disabled(store.syncing)
                if let e = store.error { Text(e).font(.footnote).foregroundStyle(.red) }
            }
            Section { Button("Sign out", role: .destructive) { auth.signOut() } }
        }
        .navigationTitle("Profile")
    }
}

/// Edit the synced GoalSettings (same fields as the MAUI/web Goals page). Ranges are min–max with 0
/// meaning off; body goals are blank for none.
struct GoalsView: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    @State private var coaching = ""
    @State private var v: [String: Double] = [:]
    @State private var warn: String?
    @State private var saved = false
    @State private var loaded = false

    private static let daily: [(String, String)] = [("protein", "Protein (g)"), ("fat", "Fat (g)"), ("fiber", "Fiber (g)")]
    private static let byDay: [(String, String)] = [("calories", "Calories"), ("carbs", "Carbs (g)"), ("water", "Water (oz)")]
    private static let body: [(String, String)] = [("goalWeightLbs", "Weight (lb)"), ("goalWaistInches", "Waist (in)"),
        ("goalBodyFatPercent", "Body fat (%)"), ("goalChestInches", "Chest (in)"), ("goalShouldersInches", "Shoulders (in)"),
        ("goalArmsInches", "Arms (in)"), ("goalThighsInches", "Thighs (in)"), ("goalCalvesInches", "Calves (in)")]

    /// Every int range key: proteinMin, caloriesRestMax, …
    private static var rangeKeys: [String] {
        daily.flatMap { ["\($0.0)Min", "\($0.0)Max"] }
            + byDay.flatMap { k in ["Rest", "Active"].flatMap { ["\(k.0)\($0)Min", "\(k.0)\($0)Max"] } }
    }

    var body: some View {
        Form {
            Section {
                TextField("Your goals, priorities and coaching style", text: $coaching, axis: .vertical).lineLimit(4...30)
            } header: { Text("Coaching goals") } footer: {
                Text("The Coach review (on Today) uses this as its success criteria and coaching style.")
            }
            Section {
                ForEach(Self.daily, id: \.0) { range($0.1, "\($0.0)Min", "\($0.0)Max") }
                LabeledContent("Added sugar ceiling (g)") { int("addedSugarMax") }
            } header: { Text("Daily macros") } footer: { Text("Min – max. 0 turns a target off.") }
            Section {
                range("Deficit (kcal)", "deficitMin", "deficitMax")
            } header: { Text("Daily calorie deficit") } footer: {
                Text("The Coach's Today check-in budgets calories as your projected burn minus this. Saved on this iPhone only.")
            }
            Section("Rest days") { ForEach(Self.byDay, id: \.0) { range($0.1, "\($0.0)RestMin", "\($0.0)RestMax") } }
            Section("Workout days") { ForEach(Self.byDay, id: \.0) { range($0.1, "\($0.0)ActiveMin", "\($0.0)ActiveMax") } }
            Section {
                ForEach(Self.body, id: \.0) { k, label in
                    OptionalNumberField(label, value: Binding(get: { v[k] }, set: { v[k] = $0 }))
                }
            } header: { Text("Body-measurement goals") } footer: {
                Text("Leave blank for no goal — reduce vs. gain is worked out from where you are now.")
            }
            if let w = warn { Section { Text(w).foregroundStyle(.red) } }
            if saved { Section { Label("Saved — syncs with your other devices", systemImage: "checkmark.circle").foregroundStyle(.green) } }
        }
        .navigationTitle("Goals")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Save", action: save) } }
        .onAppear(perform: load)
        .onChange(of: v) { saved = false }
        .onChange(of: coaching) { saved = false }
    }

    private func range(_ label: String, _ lo: String, _ hi: String) -> some View {
        LabeledContent(label) {
            HStack(spacing: 6) { int(lo); Text("–").foregroundStyle(.secondary); int(hi) }
        }
    }

    private func int(_ k: String) -> some View {
        PadNumberField("0", value: Binding<Int?>(get: { v[k].map { Int($0) } }, set: { v[k] = $0.map(Double.init) }))
            .multilineTextAlignment(.trailing).monospacedDigit().frame(width: 64)
    }

    private func load() {
        guard !loaded else { return }
        loaded = true
        v["deficitMin"] = Double(store.calorieDeficit.lowerBound); v["deficitMax"] = Double(store.calorieDeficit.upperBound)
        guard let r = store.goalsRow()?.row else { return }
        coaching = r.string("coachingGoals") ?? ""
        for k in Self.rangeKeys + ["addedSugarMax"] { v[k] = r.double(k).flatMap { $0 == 0 ? nil : $0 } }
        for (k, _) in Self.body { v[k] = r.double(k) }
        saved = false
    }

    private func save() {
        warn = nil
        guard v.values.allSatisfy({ $0 >= 0 }) else { warn = "Goals can't be negative."; return }
        for k in Self.rangeKeys where k.hasSuffix("Min") {
            let hi = String(k.dropLast(3)) + "Max"
            if let lo = v[k], let max = v[hi], max > 0, lo > max { warn = "A minimum is above its maximum."; return }
        }
        let c = coaching.trimmingCharacters(in: .whitespacesAndNewlines)
        var fields: Row = ["coachingGoals": .s(c.isEmpty ? nil : c)]
        // C# ints aren't nullable — blank is 0 (off).
        for k in Self.rangeKeys + ["addedSugarMax"] { fields[k] = .i(Int((v[k] ?? 0).rounded())) }
        for (k, _) in Self.body { fields[k] = .d(v[k]) }
        store.calorieDeficit = Int(v["deficitMin"] ?? 0)...max(Int(v["deficitMin"] ?? 0), Int(v["deficitMax"] ?? 0))
        store.saveGoals(fields)
        saved = true
    }
}

/// The MAUI Settings disclosure, for the server AI features this app uses (label scan, plate estimate).
struct AIPrivacyView: View {
    var body: some View {
        List {
            Section("Sent") {
                Text("Label and plate photos you choose to scan, plus any hint you type. Plate estimates also include today's meals and totals, your macro targets, today's planned day type and your meal tags.")
            }
            Section("Coach review (only when you run one)") {
                Text("Your coaching goals and targets, 28 days of daily food totals and energy in/out, sleep, HRV, sleeping heart rate and readiness, 8 weeks of workouts (sets and difficulty), your check-ins and body measurements.")
            }
            Section("Never sent") {
                Text("Medications, labs, cessation data, past days' notes (only today's go to the Today check-in), photos other than scans, or anything identifying.")
            }
            Section {
                Text("AI requests go through Fit Log's own server; the provider's free tier may use submitted text to improve its models, which is why medication and lab data are excluded by design.")
                    .font(.footnote).foregroundStyle(.secondary)
            }
        }
        .navigationTitle("AI & privacy")
    }
}

import SwiftUI

/// Today's link to the coach: today's check-in at a glance.
struct CoachSection: View {
    @EnvironmentObject private var store: Store
    var body: some View {
        Section {
            NavigationLink { CoachView() } label: {
                if let c = store.todaysCheck {
                    VStack(alignment: .leading, spacing: 4) {
                        CoachStatus(status: c.tone)
                        Text(c.synopsis).font(.subheadline).lineLimit(3)
                        Text(c.generatedAt.formatted(date: .omitted, time: .shortened)).font(.caption).foregroundStyle(.secondary)
                    }
                } else {
                    Label("Check in on today", systemImage: "sparkles")
                }
            }
        } header: { Text("Coach") }
    }
}

struct CoachStatus: View {
    let status: String
    var body: some View {
        let (text, icon, color): (String, String, Color) = switch status {
        case "on-track": ("On track", "checkmark.circle.fill", .green)
        case "good": ("On track", "checkmark.circle.fill", .green)
        case "adjust": ("Adjust", "arrow.triangle.2.circlepath", .orange)
        case "mixed": ("Mixed", "circle.lefthalf.filled", .orange)
        case "poor": ("Off track", "exclamationmark.triangle.fill", .red)
        default: ("Unclear", "questionmark.circle", .secondary)
        }
        Label(text, systemImage: icon).font(.headline).foregroundStyle(color)
    }
}

/// Today: a check-in on the day so far and what to do with the rest of it. Trends: the longer review.
/// Both use your coaching goals as the brief.
struct CoachView: View {
    @EnvironmentObject private var store: Store
    @State private var mode = 0
    @State private var error: String?

    var body: some View {
        List {
            Picker("View", selection: $mode) { Text("Today").tag(0); Text("Trends").tag(1) }
                .pickerStyle(.segmented)
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets())
            if mode == 0 { today } else { trends }
            Section {
                Button {
                    error = nil
                    Task { error = mode == 0 ? await store.runDailyCheck() : await store.runCoach() }
                } label: {
                    HStack {
                        Label(runTitle, systemImage: "sparkles")
                        if store.coaching { Spacer(); ProgressView() }
                    }
                }
                .disabled(store.coaching)
                if let error { Text(error).font(.footnote).foregroundStyle(.red) }
                if (store.goals?.coachingGoals ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    NavigationLink { GoalsView() } label: { Label("Add your coaching goals first", systemImage: "target") }
                }
            } footer: {
                Text(mode == 0
                     ? "Sends your coaching goals, today's notes, today's targets and day type, today's meals and drinks, energy in/out, last night's sleep, HRV, sleeping heart rate and readiness, recent workouts and the last 7 days' totals."
                     : "Sends your coaching goals, targets, 28 days of food totals and energy, sleep, HRV and sleeping heart rate, 8 weeks of workouts, your check-ins and body measurements.")
                + Text(" Never medications, labs, notes or photos.")
            }
        }
        .navigationTitle("Coach")
    }

    private var runTitle: String {
        mode == 0 ? (store.todaysCheck == nil ? "Check in on today" : "Check in again")
                  : (store.coachReport == nil ? "Get a trend review" : "Run a new trend review")
    }

    @ViewBuilder private var today: some View {
        if let c = store.todaysCheck {
            Section {
                CoachStatus(status: c.tone)
                Text(c.synopsis)
                if let r = c.remaining { Text(r).font(.subheadline.weight(.semibold)).foregroundStyle(.secondary) }
            } footer: { Text("Checked in at \(c.generatedAt.formatted(date: .omitted, time: .shortened))") }
            if !c.tips.isEmpty {
                Section("For the rest of today") { numbered(c.tips) }
            }
            if !c.watching.isEmpty {
                Section {
                    ForEach(c.watching, id: \.evidence) { Label($0.evidence, systemImage: "eye").font(.subheadline).foregroundStyle(.secondary) }
                } header: { Text("Worth watching") } footer: { Text("Not a problem today — doesn't change the status.") }
            }
        }
    }

    @ViewBuilder private var trends: some View {
        if let r = store.coachReport {
            Section {
                CoachStatus(status: r.status)
                Text(r.headline)
            } footer: { Text("Reviewed \(r.generatedAt.formatted(date: .abbreviated, time: .shortened))") }
            ForEach(r.sections) { s in
                Section(s.title) { ForEach(s.points, id: \.self) { Text($0) } }
            }
            if !r.actions.isEmpty { Section("Adjustments") { numbered(r.actions) } }
        }
    }

    private func numbered(_ items: [String]) -> some View {
        ForEach(Array(items.enumerated()), id: \.offset) { i, a in
            Label { Text(a) } icon: { Text("\(i + 1)").font(.headline).foregroundStyle(.tint) }
        }
    }
}

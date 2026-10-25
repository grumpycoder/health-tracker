import SwiftUI

/// Today's link to the coach: the last review's verdict at a glance.
struct CoachSection: View {
    @EnvironmentObject private var store: Store
    var body: some View {
        Section {
            NavigationLink { CoachView() } label: {
                if let r = store.coachReport {
                    VStack(alignment: .leading, spacing: 4) {
                        CoachStatus(status: r.status)
                        Text(r.headline).font(.subheadline).lineLimit(3)
                        Text(r.generatedAt.formatted(.relative(presentation: .named))).font(.caption).foregroundStyle(.secondary)
                    }
                } else {
                    Label("Get a coaching review", systemImage: "sparkles")
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
        case "adjust": ("Adjust", "arrow.triangle.2.circlepath", .orange)
        default: ("Unclear", "questionmark.circle", .secondary)
        }
        Label(text, systemImage: icon).font(.headline).foregroundStyle(color)
    }
}

/// The coaching review: your coaching goals + a trend summary of your data, sent to the AI.
struct CoachView: View {
    @EnvironmentObject private var store: Store
    @State private var error: String?

    var body: some View {
        List {
            if let r = store.coachReport {
                Section {
                    CoachStatus(status: r.status)
                    Text(r.headline)
                } footer: { Text("Reviewed \(r.generatedAt.formatted(date: .abbreviated, time: .shortened))") }
                ForEach(r.sections) { s in
                    Section(s.title) { ForEach(s.points, id: \.self) { Text($0) } }
                }
                if !r.actions.isEmpty {
                    Section("Adjustments") {
                        ForEach(Array(r.actions.enumerated()), id: \.offset) { i, a in
                            Label { Text(a) } icon: { Text("\(i + 1)").font(.headline).foregroundStyle(.tint) }
                        }
                    }
                }
            }
            Section {
                Button {
                    error = nil
                    Task { error = await store.runCoach() }
                } label: {
                    HStack {
                        Label(store.coachReport == nil ? "Get a coaching review" : "Run a new review", systemImage: "sparkles")
                        if store.coaching { Spacer(); ProgressView() }
                    }
                }
                .disabled(store.coaching)
                if let error { Text(error).font(.footnote).foregroundStyle(.red) }
                if (store.goals?.coachingGoals ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    NavigationLink { GoalsView() } label: { Label("Add your coaching goals first", systemImage: "target") }
                }
            } footer: {
                Text("Sends your coaching goals, targets, the last 28 days of food totals, energy, sleep, HRV and sleeping heart rate, 8 weeks of workouts, your check-ins and body measurements. Never medications, labs, notes or photos.")
            }
        }
        .navigationTitle("Coach")
    }
}

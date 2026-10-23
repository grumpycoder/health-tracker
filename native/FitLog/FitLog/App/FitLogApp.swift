import AuthenticationServices
import SwiftUI

@main
struct FitLogApp: App {
    @StateObject private var auth: Auth
    @StateObject private var store: Store
    @StateObject private var workout: WorkoutController

    init() {
        let a = Auth()
        _auth = StateObject(wrappedValue: a)
        let st = Store(auth: a)
        _store = StateObject(wrappedValue: st)
        _workout = StateObject(wrappedValue: WorkoutController(store: st))
    }
    @Environment(\.scenePhase) private var phase

    var body: some Scene {
        WindowGroup {
            Group {
                if auth.isSignedIn { RootView() } else { SignInView() }
            }
            .environmentObject(auth)
            .environmentObject(store)
            .environmentObject(workout)
            .task(id: auth.isSignedIn) {
                guard auth.isSignedIn else { return }
                await store.health.requestAccessAndLoad()
                await store.refresh(auth: auth)
            }
            .onChange(of: phase) { _, p in
                if p == .active, auth.isSignedIn { Task { await store.refresh(auth: auth) } }
            }
        }
    }
}

struct RootView: View {
    var body: some View {
        TabView {
            TodayView().tabItem { Label("Today", systemImage: "house") }
            DayLogView().tabItem { Label("Meals", systemImage: "fork.knife") }
            WorkoutTab().tabItem { Label("Workout", systemImage: "figure.strengthtraining.traditional") }
            BodyView().tabItem { Label("Body", systemImage: "scalemass") }
            SleepView().tabItem { Label("Sleep", systemImage: "bed.double") }
        }
    }
}

struct SignInView: View {
    @EnvironmentObject private var auth: Auth
    @Environment(\.webAuthenticationSession) private var session
    @State private var error: String?

    var body: some View {
        VStack(spacing: 20) {
            Spacer()
            Image(systemName: "figure.strengthtraining.traditional").font(.system(size: 56)).foregroundStyle(.tint)
            Text("Fit Log").font(.largeTitle.bold())
            Text("Native · preview").foregroundStyle(.secondary)
            Spacer()
            Button {
                Task {
                    do { try await auth.signIn(using: session) } catch { self.error = error.localizedDescription }
                }
            } label: { Text("Sign in with Microsoft").frame(maxWidth: .infinity) }
                .buttonStyle(.borderedProminent).controlSize(.large)
            if let error { Text(error).font(.footnote).foregroundStyle(.red) }
        }
        .padding()
    }
}

struct AccountView: View {
    @EnvironmentObject private var auth: Auth
    @EnvironmentObject private var store: Store

    var body: some View {
            List {
                Section("Sync") {
                    LabeledContent("Last synced", value: store.lastSync?.formatted(date: .abbreviated, time: .shortened) ?? "—")
                    LabeledContent("Meals", value: "\(store.meals.count)")
                    LabeledContent("Drinks", value: "\(store.drinks.count)")
                    LabeledContent("Day logs", value: "\(store.logs.count)")
                    if let r = store.sync.lastResult { LabeledContent("Last sync", value: r) }
                    Button(store.syncing ? "Syncing…" : "Sync now") { Task { await store.refresh(auth: auth) } }
                        .disabled(store.syncing)
                    if let e = store.error { Text(e).font(.footnote).foregroundStyle(.red) }
                }
                Section {
                    Text("Data is stored on this iPhone and synced with your other devices. Editing arrives in the next update.")
                        .font(.footnote).foregroundStyle(.secondary)
                }
                Section { Button("Sign out", role: .destructive) { auth.signOut() } }
            }
            .navigationTitle("Account")
    }
}

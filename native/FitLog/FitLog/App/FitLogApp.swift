import AuthenticationServices
import SwiftUI
import UserNotifications

@main
struct FitLogApp: App {
    @StateObject private var auth: Auth
    @StateObject private var store: Store
    @StateObject private var workout: WorkoutController
    @StateObject private var router = Router()

    init() {
        let a = Auth()
        _auth = StateObject(wrappedValue: a)
        let st = Store(auth: a)
        _store = StateObject(wrappedValue: st)
        _workout = StateObject(wrappedValue: WorkoutController(store: st))
        // Set during launch so reminders show while the app is open and a tap is delivered.
        UNUserNotificationCenter.current().delegate = NotificationDelegate.shared
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
            .environmentObject(router)
            .onOpenURL { router.open($0) }
            .onAppear { NotificationDelegate.shared.onOpen = { [router] in router.tab = 0 } }
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

/// Where a deep link (e.g. the widget's budget → fitlogn://add-meal) should take you.
@MainActor
final class Router: ObservableObject {
    @Published var tab = 0
    @Published var addMeal = false
    func open(_ url: URL) {
        guard url.scheme == "fitlogn" else { return }
        if url.host == "add-meal" { tab = 0; addMeal = true }
    }
}

struct RootView: View {
    @EnvironmentObject private var router: Router
    var body: some View {
        TabView(selection: $router.tab) {
            TodayView().tabItem { Label("Today", systemImage: "house") }.tag(0)
            DayLogView().tabItem { Label("Meals", systemImage: "fork.knife") }.tag(1)
            WorkoutTab().tabItem { Label("Workout", systemImage: "figure.strengthtraining.traditional") }.tag(2)
            BodyView().tabItem { Label("Body", systemImage: "scalemass") }.tag(3)
            SleepView().tabItem { Label("Sleep", systemImage: "bed.double") }.tag(4)
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

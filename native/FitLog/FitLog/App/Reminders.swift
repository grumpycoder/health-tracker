import Foundation
import UserNotifications

/// Keeps the OS's pending local notifications equal to the reminder plan (Meds.plan). Rebuilt after
/// every reload; only touches the system when the plan actually changed.
@MainActor
final class ReminderNotifier: ObservableObject {
    /// nil until asked; false when notifications are turned off for this app in Settings.
    @Published private(set) var permitted: Bool?
    /// Lets this app stay quiet while the MAUI app still sends the same reminders.
    @Published var enabled = UserDefaults.standard.object(forKey: "remindersEnabled") as? Bool ?? true {
        didSet { UserDefaults.standard.set(enabled, forKey: "remindersEnabled"); last = nil }
    }
    private var last: [Meds.Alert]?
    private let center = UNUserNotificationCenter.current()

    private var running: Task<Void, Never>?

    /// Queue a plan; updates run one at a time so two quick reloads can't interleave adds.
    func update(_ plan: [Meds.Alert]) {
        let previous = running
        running = Task { await previous?.value; await apply(plan) }
    }

    private func apply(_ plan: [Meds.Alert]) async {
        let wanted = enabled ? plan : []
        guard wanted != last else { return }
        last = wanted
        if !wanted.isEmpty {
            let granted = (try? await center.requestAuthorization(options: [.alert, .sound, .badge])) ?? false
            permitted = granted
            guard granted else { return }
        }
        // Replace them all: a renamed med or changed dose keeps its ids but needs new text.
        center.removeAllPendingNotificationRequests()
        for a in wanted {
            let content = UNMutableNotificationContent()
            content.title = a.title
            content.body = a.body
            content.sound = .default
            let when = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute], from: a.fire)
            try? await center.add(UNNotificationRequest(identifier: a.id, content: content,
                                                        trigger: UNCalendarNotificationTrigger(dateMatching: when, repeats: false)))
        }
    }
}

/// Shows reminders while the app is open, and opens Today (where due doses are) when one is tapped.
final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate {
    static let shared = NotificationDelegate()
    var onOpen: (@MainActor () -> Void)?

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async
        -> UNNotificationPresentationOptions { [.banner, .list, .sound] }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
        await MainActor.run { onOpen?() }
    }
}

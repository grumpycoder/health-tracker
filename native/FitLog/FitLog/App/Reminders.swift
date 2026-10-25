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
            if let d = a.dose {
                content.categoryIdentifier = NotificationDelegate.category(site: d.site)
                content.userInfo = ["scheduleId": d.scheduleId.uuidString, "day": d.day.iso, "site": d.site ?? ""]
            }
            let when = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute], from: a.fire)
            try? await center.add(UNNotificationRequest(identifier: a.id, content: content,
                                                        trigger: UNCalendarNotificationTrigger(dateMatching: when, repeats: false)))
        }
    }
}

/// Shows reminders while the app is open, logs a dose from a reminder's Log action (in the
/// background), and otherwise opens Today — with the site picker when "Choose site…" was tapped.
final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate {
    static let shared = NotificationDelegate()
    /// Opens Today; the schedule id asks for its injection-site picker.
    var onOpen: (@MainActor (UUID?) -> Void)?
    weak var store: Store?

    private static let log = "log", choose = "choose"

    /// Action titles are fixed per category, so each injection site gets its own ("Log — Left glute").
    static func category(site: String?) -> String {
        site.flatMap { Meds.injectionSites.firstIndex(of: $0) }.map { "dose.site.\($0)" } ?? "dose"
    }

    static func registerCategories() {
        let choose = UNNotificationAction(identifier: choose, title: "Choose site…", options: [.foreground])
        let plain = UNNotificationCategory(identifier: "dose", actions: [UNNotificationAction(identifier: log, title: "Log dose")],
                                           intentIdentifiers: [])
        let sites = Meds.injectionSites.enumerated().map { i, site in
            UNNotificationCategory(identifier: "dose.site.\(i)",
                                   actions: [UNNotificationAction(identifier: log, title: "Log — \(site)"), choose], intentIdentifiers: [])
        }
        UNUserNotificationCenter.current().setNotificationCategories(Set([plain] + sites))
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async
        -> UNNotificationPresentationOptions { [.banner, .list, .sound] }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
        let info = response.notification.request.content.userInfo
        let schedule = (info["scheduleId"] as? String).flatMap(UUID.init(uuidString:))
        switch response.actionIdentifier {
        case Self.log:
            guard let schedule, let day = (info["day"] as? String).flatMap(Day.init(iso:)) else { return }
            let site = (info["site"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            await store?.logDoseFromReminder(schedule, day: day, site: site)
        case Self.choose:
            await MainActor.run { onOpen?(schedule) }
        default:
            await MainActor.run { onOpen?(nil) }
        }
    }
}

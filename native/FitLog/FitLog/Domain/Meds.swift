import Foundation

// Medications and reminders, ported from the MAUI Medication/Reminders pages and ReminderNotifier.
// Schedules (MedicationSchedule) drive one-tap logging and dose reminders; doses are
// MedicationEntry rows; routine reminders (measurement, lab check, …) are ReminderSetting rows.

/// C# ReminderRepeat — Biweekly was appended, so the raw values aren't in display order.
enum ReminderRepeat: Int, Decodable, CaseIterable, Identifiable {
    case once, daily, weekly, monthly, biweekly
    var id: Int { rawValue }
    var label: String { ["Once", "Daily", "Weekly", "Monthly", "Biweekly"][rawValue] }
    /// The order the pickers list them in.
    static let ordered: [ReminderRepeat] = [.once, .daily, .weekly, .biweekly, .monthly]
}

enum MedicationForm: Int, Decodable, CaseIterable, Identifiable {
    case oral, injection, topical
    var id: Int { rawValue }
    var label: String { ["Oral", "Injection", "Topical"][rawValue] }
    var glyph: String { ["💊", "💉", "🧴"][rawValue] }
}

/// C# TimeOnly ("HH:mm:ss", optional fraction).
struct ClockTime: Hashable, Decodable {
    var hour: Int, minute: Int
    init(hour: Int, minute: Int) { self.hour = hour; self.minute = minute }
    init(_ date: Date) {
        let c = Calendar.current.dateComponents([.hour, .minute], from: date)
        self.init(hour: c.hour ?? 0, minute: c.minute ?? 0)
    }
    init(from decoder: Decoder) throws {
        let s = try decoder.singleValueContainer().decode(String.self)
        let p = s.split(separator: ":").prefix(2).compactMap { Int($0) }
        guard p.count == 2 else { throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath, debugDescription: s)) }
        self.init(hour: p[0], minute: p[1])
    }
    var wire: JSON { .string(String(format: "%02d:%02d:00", hour, minute)) }
    func on(_ day: Day) -> Date { Calendar.current.date(bySettingHour: hour, minute: minute, second: 0, of: day.date)! }
    /// Today at this time — what a DatePicker binds to.
    var date: Date { on(.today) }
    var label: String { date.formatted(date: .omitted, time: .shortened) }
}

struct MedicationSchedule: Decodable, Identifiable {
    let id: UUID
    let name: String
    let dose: String?
    let form: MedicationForm
    let active: Bool
    let `repeat`: ReminderRepeat
    let startDate: Day
    let endDate: Day?
    let reminderTime: ClockTime
    let notificationId: Int

    /// "100mg · Weekly 9:00 AM · until Dec 1"
    var summary: String {
        [dose, "\(self.repeat.label) \(reminderTime.label)",
         endDate.map { "until \($0.date.formatted(.dateTime.month(.abbreviated).day()))" }].compactMap { $0 }.joined(separator: " · ")
    }

    /// Active and not past its end date (the MAUI Reminders filter).
    func current(today: Day = .today) -> Bool { active && (endDate.map { $0 >= today } ?? true) }
}

struct MedicationEntry: Decodable, Identifiable {
    let id: UUID
    let name: String
    let dose: String?
    let frequency: String?
    let takenAt: Date
    let injectionSite: String?
    let reactionNotes: String?
}

struct ReminderSetting: Decodable, Identifiable {
    let id: UUID
    let key: String
    let `repeat`: ReminderRepeat
    let time: ClockTime
    let active: Bool
    let notificationId: Int
    /// 0 = Sunday … 6 = Saturday; nil is Sunday.
    let dayOfWeek: Int?

    var label: String {
        ["measurement": "Body Measurement", "labCheck": "Lab Check", "weeklyReview": "Weekly Review",
         "progressPhoto": "Progress Photo"][key] ?? key
    }
    var order: Int { ["measurement": 0, "progressPhoto": 1, "labCheck": 2, "weeklyReview": 3][key] ?? 9 }
}

enum Meds {
    static let injectionSites = ["Left delt", "Right delt", "Left glute", "Right glute", "Left quad", "Right quad",
                                 "Left ventroglute", "Right ventroglute"]
    static let weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]

    /// The mirror of the last site used for this medication, so injections rotate sides.
    static func suggestedSite(for name: String, doses: [MedicationEntry]) -> String {
        let last = doses.filter { $0.name.caseInsensitiveCompare(name) == .orderedSame && !($0.injectionSite ?? "").isEmpty }
            .max { $0.takenAt < $1.takenAt }?.injectionSite
        guard let last else { return injectionSites[0] }
        if last.contains("Left") { return last.replacingOccurrences(of: "Left", with: "Right") }
        if last.contains("Right") { return last.replacingOccurrences(of: "Right", with: "Left") }
        return last
    }

    /// Latest day each medication (by name, case-insensitive) was logged.
    static func lastTaken(_ doses: [MedicationEntry]) -> [String: Day] {
        doses.reduce(into: [:]) { m, d in
            let k = d.name.lowercased(), day = Day(d.takenAt)
            if m[k].map({ day > $0 }) ?? true { m[k] = day }
        }
    }

    /// The schedule's most recent due date on or before today, or nil before it starts
    /// (ReminderNotifier.LastDue). A dose is outstanding while nothing's been logged since.
    static func lastDue(_ s: MedicationSchedule, today: Day = .today) -> Day? {
        guard s.active, s.startDate <= today else { return nil }
        let days = Calendar.current.dateComponents([.day], from: s.startDate.date, to: today.date).day ?? 0
        switch s.repeat {
        case .once: return s.startDate
        case .daily: return today
        case .weekly: return today.adding(-(days % 7))
        case .biweekly: return today.adding(-(days % 14))
        case .monthly:
            var d = s.startDate   // stepped one month at a time, so month-end clamping matches C# AddMonths
            while step(d, .monthly) <= today { d = step(d, .monthly) }
            return d
        }
    }

    /// Doses due today or overdue and not yet logged.
    static func outstanding(_ schedules: [MedicationSchedule], doses: [MedicationEntry], today: Day = .today) -> [MedicationSchedule] {
        let taken = lastTaken(doses)
        return schedules.filter { s in
            guard s.current(today: today), let due = lastDue(s, today: today) else { return false }
            return taken[s.name.lowercased()].map { $0 < due } ?? true
        }
    }

    static func step(_ d: Day, _ r: ReminderRepeat) -> Day {
        switch r {
        case .once: d
        case .daily: d.adding(1)
        case .weekly: d.adding(7)
        case .biweekly: d.adding(14)
        case .monthly: Day(Calendar.current.date(byAdding: .month, value: 1, to: d.date)!)
        }
    }

    // MARK: notification plan

    struct Alert: Hashable {
        let id: String
        let title: String
        let body: String
        let fire: Date
    }

    /// iOS keeps at most 64 pending notifications per app.
    static let maxPending = 60

    /// Upcoming dose reminders as concrete dates (not OS repeats), so a dose already logged today
    /// doesn't still nag, Monthly lands on the right day, and an end date stops them.
    static func doseAlerts(_ s: MedicationSchedule, lastTaken: Day?, now: Date = Date(), count: Int) -> [Alert] {
        let today = Day(now)
        guard s.current(today: today) else { return [] }
        var day = s.startDate
        if let period = [ReminderRepeat.daily: 1, .weekly: 7, .biweekly: 14][s.repeat], day < today.adding(-1) {
            // Jump to the last occurrence before yesterday instead of stepping through years of days.
            let days = Calendar.current.dateComponents([.day], from: day.date, to: today.adding(-1).date).day ?? 0
            day = day.adding(days / period * period)
        }
        while s.reminderTime.on(day) < now && s.repeat != .once { day = step(day, s.repeat) }
        var out: [Alert] = []
        while out.count < count {
            if let end = s.endDate, day > end { break }
            let fire = s.reminderTime.on(day)
            if fire > now && !(lastTaken.map { $0 >= day } ?? false) {
                out.append(Alert(id: "dose.\(s.id.uuidString).\(day.iso)", title: "Take \(s.name)", body: s.dose ?? "", fire: fire))
            }
            if s.repeat == .once { break }
            day = step(day, s.repeat)
        }
        return out
    }

    /// Upcoming routine reminders. Weekly lands on its fixed weekday; the others skip the current
    /// period when the task is already done (`lastDone` within the interval), like MainLayout.
    static func routineAlerts(_ s: ReminderSetting, lastDone: Day?, now: Date = Date(), count: Int) -> [Alert] {
        guard s.active else { return [] }
        let today = Day(now)
        var day: Day
        switch s.repeat {
        case .weekly:
            day = today
            let target = (s.dayOfWeek ?? 0) + 1   // Calendar weekday: 1 = Sunday
            while Calendar.current.component(.weekday, from: day.date) != target { day = day.adding(1) }
        default:
            let interval = [ReminderRepeat.daily: 1, .biweekly: 14, .monthly: 30][s.repeat] ?? 7
            let done = lastDone.map { $0 >= today.adding(-interval) } ?? false
            day = done ? Day(now.addingTimeInterval(Double(interval) * 86_400)) : today
        }
        var out: [Alert] = []
        while out.count < count {
            let fire = s.time.on(day)
            if fire > now { out.append(Alert(id: "routine.\(s.id.uuidString).\(day.iso)", title: s.label, body: "", fire: fire)) }
            if s.repeat == .once { break }
            day = step(day, s.repeat)
        }
        return out
    }

    /// Every upcoming alert, soonest first, within the OS limit.
    static func plan(schedules: [MedicationSchedule], doses: [MedicationEntry], settings: [ReminderSetting],
                     lastDone: [String: Day], now: Date = Date()) -> [Alert] {
        let taken = lastTaken(doses)
        let all = schedules.flatMap { doseAlerts($0, lastTaken: taken[$0.name.lowercased()], now: now, count: 14) }
            + settings.flatMap { routineAlerts($0, lastDone: lastDone[$0.key], now: now, count: 8) }
        return Array(all.sorted { $0.fire < $1.fire }.prefix(maxPending))
    }
}

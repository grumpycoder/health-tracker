import SwiftUI

/// Today's doses due (or overdue) with one-tap logging, plus links to the full screens.
struct MedsTodaySection: View {
    @EnvironmentObject private var store: Store
    /// Presented by the parent list — a dialog attached inside a row is dismissed when the list re-renders.
    @Binding var siteFor: MedicationSchedule?

    var body: some View {
        Section {
            let due = store.dueDoses
            if due.isEmpty {
                let logged = store.medSchedules.contains { s in s.current() && store.doses.contains { Day($0.takenAt) == .today && $0.name.caseInsensitiveCompare(s.name) == .orderedSame } }
                if logged { Label("All doses logged for today", systemImage: "checkmark.circle").foregroundStyle(.green) }
            }
            ForEach(due) { s in
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("\(s.form.glyph) \(s.name)").font(.headline)
                        Text(dueLabel(s)).font(.caption).foregroundStyle(isOverdue(s) ? .orange : .secondary)
                    }
                    Spacer()
                    Button("Log") { log(s) }.buttonStyle(.borderedProminent)
                }
            }
            NavigationLink { MedsView() } label: { Label("Medications", systemImage: "pills") }
            NavigationLink { RemindersView() } label: { Label("Reminders", systemImage: "bell") }
        } header: { Text("Medications") }
    }

    private func log(_ s: MedicationSchedule) {
        if s.form == .injection { siteFor = s } else { store.logDose(s) }
    }

    private func isOverdue(_ s: MedicationSchedule) -> Bool { Meds.lastDue(s).map { $0 < .today } ?? false }

    private func dueLabel(_ s: MedicationSchedule) -> String {
        if let d = Meds.lastDue(s), d < .today { return "Overdue — was due \(d.date.formatted(.dateTime.month(.abbreviated).day()))" }
        return "\(s.dose.map { "\($0) · " } ?? "")due today \(s.reminderTime.label)"
    }
}

extension View {
    /// Pick the injection site for a schedule's dose, the suggested (rotated) side first, then log it.
    func injectionSitePicker(for schedule: Binding<MedicationSchedule?>) -> some View {
        modifier(InjectionSitePicker(schedule: schedule))
    }
}

private struct InjectionSitePicker: ViewModifier {
    @EnvironmentObject private var store: Store
    @Binding var schedule: MedicationSchedule?

    func body(content: Content) -> some View {
        content.confirmationDialog("Injection site", isPresented: Binding(get: { schedule != nil }, set: { if !$0 { schedule = nil } }),
                                   titleVisibility: .visible, presenting: schedule) { s in
            let suggested = Meds.suggestedSite(for: s.name, doses: store.doses)
            Button("\(suggested) (suggested)") { store.logDose(s, site: suggested) }
            ForEach(Meds.injectionSites.filter { $0 != suggested }, id: \.self) { site in
                Button(site) { store.logDose(s, site: site) }
            }
        } message: { s in Text("Log \(s.name)\(s.dose.map { " \($0)" } ?? "") now") }
    }
}

// MARK: - Medications

struct MedsView: View {
    @EnvironmentObject private var store: Store
    @State private var editing: MedTarget?
    @State private var editingDose: MedTarget?
    @State private var siteFor: MedicationSchedule?
    @State private var logged: String?

    var body: some View {
        List {
            Section {
                let active = store.medSchedules.filter(\.active)
                if active.isEmpty { Text("No saved medications yet. Add one to log it with one tap and get reminders.").foregroundStyle(.secondary) }
                ForEach(active) { s in
                    HStack {
                        Button { editing = .edit(s.id) } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("\(s.form.glyph) \(s.name)").font(.headline)
                                Text([s.dose, "\(s.repeat.label) \(s.reminderTime.label)",
                                      s.endDate.map { "until \($0.date.formatted(.dateTime.month(.abbreviated).day()))" }]
                                    .compactMap { $0 }.joined(separator: " · "))
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        Button("Log") {
                            if s.form == .injection { siteFor = s } else { store.logDose(s); logged = "Logged \(s.name)" }
                        }
                        .buttonStyle(.bordered)
                    }
                    .swipeActions { Button("Delete", role: .destructive) { store.deleteMedSchedule(s.id) } }
                }
                Button { editing = .new } label: { Label("Add medication", systemImage: "plus.circle.fill") }
                if let logged { Text("\(logged) ✓").font(.footnote).foregroundStyle(.green) }
            } header: { Text("My medications") }

            Section {
                Button { editingDose = .new } label: { Label("Log a one-off dose", systemImage: "square.and.pencil") }
            }

            Section("Recent doses") {
                if store.doses.isEmpty { Text("No medications logged yet.").foregroundStyle(.secondary) }
                ForEach(store.doses.prefix(20)) { d in
                    Button { editingDose = .edit(d.id) } label: { DoseRow(d: d) }
                        .buttonStyle(.plain)
                        .swipeActions { Button("Delete", role: .destructive) { store.deleteDose(d.id) } }
                }
            }
        }
        .navigationTitle("Medications")
        .injectionSitePicker(for: $siteFor)
        .sheet(item: $editing) { MedScheduleEditor(target: $0) }
        .sheet(item: $editingDose) { DoseEditor(target: $0) }
    }
}

enum MedTarget: Identifiable {
    case new, edit(UUID)
    var id: String { if case .edit(let u) = self { u.uuidString } else { "new" } }
    var editId: UUID? { if case .edit(let u) = self { u } else { nil } }
}

struct DoseRow: View {
    let d: MedicationEntry
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(d.name).font(.headline)
                if let dose = d.dose { Text(dose).foregroundStyle(.secondary) }
                Spacer()
                Text(d.takenAt.formatted(.dateTime.month(.abbreviated).day().hour().minute())).font(.caption).foregroundStyle(.secondary)
            }
            let bits = [d.injectionSite.map { "@ \($0)" }, d.reactionNotes].compactMap { $0 }.filter { !$0.isEmpty }
            if !bits.isEmpty { Text(bits.joined(separator: " · ")).font(.caption).foregroundStyle(.secondary) }
        }
    }
}

struct MedScheduleEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let target: MedTarget
    @State private var name = ""
    @State private var dose = ""
    @State private var form = MedicationForm.oral
    @State private var repeatEvery = ReminderRepeat.weekly
    @State private var start = Date()
    @State private var hasEnd = false
    @State private var end = Date()
    @State private var time = ClockTime(hour: 9, minute: 0).date
    @State private var warn: String?
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name (e.g. Vitamin D)", text: $name)
                    TextField("Dose (e.g. 100mg / 0.5mL)", text: $dose)
                    Picker("Form", selection: $form) { ForEach(MedicationForm.allCases) { Text("\($0.glyph) \($0.label)").tag($0) } }
                }
                Section {
                    Picker("Repeat", selection: $repeatEvery) { ForEach(ReminderRepeat.ordered) { Text($0.label).tag($0) } }
                    DatePicker("Start", selection: $start, displayedComponents: .date)
                    Toggle("End date", isOn: $hasEnd)
                    if hasEnd { DatePicker("End", selection: $end, in: start..., displayedComponents: .date) }
                    DatePicker("Reminder time", selection: $time, displayedComponents: .hourAndMinute)
                } footer: { Text("You'll get a reminder at this time on each due day until it's logged.") }
                if let w = warn { Section { Text(w).foregroundStyle(.red) } }
            }
            .navigationTitle(target.editId == nil ? "Add medication" : "Edit medication")
            .navigationBarTitleDisplayMode(.inline)
            .onAppear(perform: load)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button("Save", action: save) }
            }
        }
    }

    private func load() {
        guard !loaded else { return }
        loaded = true
        guard let id = target.editId, let s = store.medSchedules.first(where: { $0.id == id }) else { return }
        name = s.name; dose = s.dose ?? ""; form = s.form; repeatEvery = s.repeat
        start = s.startDate.date; hasEnd = s.endDate != nil; end = s.endDate?.date ?? Date(); time = s.reminderTime.date
    }

    private func save() {
        let n = name.trimmingCharacters(in: .whitespaces), d = dose.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { warn = "Enter a medication name."; return }
        store.saveMedSchedule(id: target.editId, fields: [
            "name": .string(n), "dose": .s(d.isEmpty ? nil : d), "form": .i(form.rawValue), "repeat": .i(repeatEvery.rawValue),
            "startDate": Wire.day(Day(start)), "endDate": hasEnd ? Wire.day(Day(max(end, start))) : .null,
            "reminderTime": ClockTime(time).wire,
        ])
        dismiss()
    }
}

/// Log a one-off dose, or edit a logged one.
struct DoseEditor: View {
    @EnvironmentObject private var store: Store
    @Environment(\.dismiss) private var dismiss
    let target: MedTarget
    @State private var name = ""
    @State private var dose = ""
    @State private var frequency = ""
    @State private var takenAt = Date()
    @State private var site = ""
    @State private var notes = ""
    @State private var warn: String?
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Medication", text: $name)
                    TextField("Dose", text: $dose)
                    TextField("Frequency (e.g. weekly)", text: $frequency)
                    DatePicker("Taken", selection: $takenAt)
                }
                Section {
                    Picker("Injection site", selection: $site) {
                        Text("None").tag("")
                        ForEach(Meds.injectionSites, id: \.self) { Text($0).tag($0) }
                    }
                    TextField("Reaction / soreness notes", text: $notes, axis: .vertical)
                }
                if let w = warn { Section { Text(w).foregroundStyle(.red) } }
            }
            .navigationTitle(target.editId == nil ? "Log dose" : "Edit dose")
            .navigationBarTitleDisplayMode(.inline)
            .onAppear(perform: load)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button("Save", action: save) }
            }
        }
    }

    private func load() {
        guard !loaded else { return }
        loaded = true
        guard let id = target.editId, let r = store.doseRow(id) else { return }
        name = r.string("name") ?? ""; dose = r.string("dose") ?? ""; frequency = r.string("frequency") ?? ""
        takenAt = r.date("takenAt") ?? Date(); site = r.string("injectionSite") ?? ""; notes = r.string("reactionNotes") ?? ""
    }

    private func save() {
        func opt(_ s: String) -> JSON { let t = s.trimmingCharacters(in: .whitespacesAndNewlines); return .s(t.isEmpty ? nil : t) }
        let n = name.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { warn = "Enter a medication name."; return }
        store.saveDose(id: target.editId, fields: ["name": .string(n), "dose": opt(dose), "frequency": opt(frequency),
                                                   "takenAt": Wire.local(takenAt), "injectionSite": opt(site), "reactionNotes": opt(notes)])
        dismiss()
    }
}

// MARK: - Reminders

/// Routine reminders (measurement, lab check, …) plus this app's notification switch. Dose
/// reminders come from each medication's schedule.
struct RemindersView: View {
    @EnvironmentObject private var store: Store

    var body: some View {
        List {
            Section {
                Toggle("Send reminders from this app", isOn: Binding(get: { store.reminders.enabled },
                                                                     set: { store.reminders.enabled = $0; store.rescheduleReminders() }))
                if store.reminders.permitted == false {
                    Text("Notifications are off for Fit Log N. Turn them on in Settings → Notifications.").font(.footnote).foregroundStyle(.orange)
                }
            } footer: {
                Text("Dose reminders follow each medication's schedule and skip a dose you've already logged. If the old Fit Log app also sends reminders, turn its notifications off so you don't get two.")
            }
            Section("Routine reminders") {
                if store.reminderSettings.isEmpty { Text("None synced yet.").foregroundStyle(.secondary) }
                ForEach(store.reminderSettings) { RoutineReminderRow(s: $0) }
            }
        }
        .navigationTitle("Reminders")
    }
}

private struct RoutineReminderRow: View {
    @EnvironmentObject private var store: Store
    let s: ReminderSetting

    var body: some View {
        VStack(alignment: .leading) {
            Toggle(s.label, isOn: Binding(get: { s.active }, set: { store.updateReminderSetting(s.id, ["active": .bool($0)]) }))
                .font(.headline)
            if s.active {
                Picker("Repeat", selection: Binding(get: { s.repeat }, set: { store.updateReminderSetting(s.id, ["repeat": .i($0.rawValue)]) })) {
                    ForEach([ReminderRepeat.daily, .weekly, .monthly]) { Text($0.label).tag($0) }
                }
                if s.repeat == .weekly {
                    Picker("Day", selection: Binding(get: { s.dayOfWeek ?? 0 }, set: { store.updateReminderSetting(s.id, ["dayOfWeek": .i($0)]) })) {
                        ForEach(0..<7, id: \.self) { Text(Meds.weekdays[$0]).tag($0) }
                    }
                }
                DatePicker("Time", selection: Binding(get: { s.time.date }, set: { store.updateReminderSetting(s.id, ["time": ClockTime($0).wire]) }),
                           displayedComponents: .hourAndMinute)
            }
        }
    }
}

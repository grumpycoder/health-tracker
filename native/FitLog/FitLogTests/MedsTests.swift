import XCTest
@testable import FitLog

final class MedsTests: XCTestCase {
    private func schedule(_ name: String = "Test C", repeat r: ReminderRepeat, start: String, end: String? = nil,
                          time: String = "09:00:00", active: Bool = true) throws -> MedicationSchedule {
        let json = """
        {"id":"\(UUID().uuidString)","name":"\(name)","dose":"100mg","form":1,"active":\(active),"repeat":\(r.rawValue),
         "startDate":"\(start)","endDate":\(end.map { "\"\($0)\"" } ?? "null"),"reminderTime":"\(time)","notificationId":42,
         "isDeleted":false,"somethingUnmodeled":"kept"}
        """
        return try Wire.decoder.decode(MedicationSchedule.self, from: Data(json.utf8))
    }

    private func dose(_ name: String, at: String, site: String? = nil) throws -> MedicationEntry {
        let json = """
        {"id":"\(UUID().uuidString)","name":"\(name)","takenAt":"\(at)","injectionSite":\(site.map { "\"\($0)\"" } ?? "null")}
        """
        return try Wire.decoder.decode(MedicationEntry.self, from: Data(json.utf8))
    }

    private func at(_ s: String) -> Date { Wire.parse(s)! }
    private let oct25 = Day(y: 2026, m: 10, d: 25)   // a Sunday

    func testDecodesServerRow() throws {
        let s = try schedule(repeat: .biweekly, start: "2026-10-01", time: "21:30:00.0000000")
        XCTAssertEqual(s.repeat, .biweekly)
        XCTAssertEqual(s.form, .injection)
        XCTAssertEqual(s.reminderTime, ClockTime(hour: 21, minute: 30))
        XCTAssertEqual(ClockTime(hour: 7, minute: 5).wire, .string("07:05:00"))
    }

    func testLastDue_matchesReminderNotifier() throws {
        XCTAssertEqual(Meds.lastDue(try schedule(repeat: .daily, start: "2026-01-01"), today: oct25), oct25)
        XCTAssertEqual(Meds.lastDue(try schedule(repeat: .weekly, start: "2026-10-01"), today: oct25), Day(y: 2026, m: 10, d: 22))
        XCTAssertEqual(Meds.lastDue(try schedule(repeat: .biweekly, start: "2026-10-01"), today: oct25), Day(y: 2026, m: 10, d: 15))
        XCTAssertNil(Meds.lastDue(try schedule(repeat: .daily, start: "2026-10-26"), today: oct25), "not started")
        XCTAssertNil(Meds.lastDue(try schedule(repeat: .daily, start: "2026-10-01", active: false), today: oct25))
        // Monthly steps one month at a time, so Jan 31 clamps to Feb 28 and stays on the 28th (like C# AddMonths).
        XCTAssertEqual(Meds.lastDue(try schedule(repeat: .monthly, start: "2026-01-31"), today: Day(y: 2026, m: 4, d: 10)),
                       Day(y: 2026, m: 3, d: 28))
    }

    func testOutstanding_untilLoggedSinceDue_caseInsensitive() throws {
        let weekly = try schedule("Test C", repeat: .weekly, start: "2026-10-01")   // due Oct 22
        XCTAssertEqual(Meds.outstanding([weekly], doses: [], today: oct25).count, 1, "overdue stays visible")
        XCTAssertEqual(Meds.outstanding([weekly], doses: [try dose("test c", at: "2026-10-21T08:00:00")], today: oct25).count, 1,
                       "logged before the due date doesn't count")
        XCTAssertTrue(Meds.outstanding([weekly], doses: [try dose("test c", at: "2026-10-23T08:00:00")], today: oct25).isEmpty)
        let ended = try schedule(repeat: .daily, start: "2026-10-01", end: "2026-10-24")
        XCTAssertTrue(Meds.outstanding([ended], doses: [], today: oct25).isEmpty)
    }

    func testSuggestedSite_mirrorsLastSiteForThatMed() throws {
        let doses = [try dose("Test C", at: "2026-10-15T08:00:00", site: "Left glute"),
                     try dose("Test C", at: "2026-10-22T08:00:00", site: "Right delt"),
                     try dose("Other", at: "2026-10-24T08:00:00", site: "Right quad")]
        XCTAssertEqual(Meds.suggestedSite(for: "test c", doses: doses), "Left delt")
        XCTAssertEqual(Meds.suggestedSite(for: "New", doses: doses), Meds.injectionSites[0])
    }

    func testDoseAlerts_skipTodayOnceLogged_andStopAtEndDate() throws {
        let daily = try schedule(repeat: .daily, start: "2026-01-01", end: "2026-10-28", time: "21:00:00")
        let now = at("2026-10-25T12:00:00")
        let fresh = Meds.doseAlerts(daily, lastTaken: nil, now: now, count: 14)
        XCTAssertEqual(fresh.map(\.fire), ["2026-10-25T21:00:00", "2026-10-26T21:00:00", "2026-10-27T21:00:00", "2026-10-28T21:00:00"].map(at))
        XCTAssertEqual(fresh.first?.title, "Take Test C")
        XCTAssertEqual(fresh.first?.body, "100mg")
        let logged = Meds.doseAlerts(daily, lastTaken: oct25, now: now, count: 14)
        XCTAssertEqual(logged.first?.fire, at("2026-10-26T21:00:00"), "today's dose already logged → no nag tonight")
    }

    func testDoseAlerts_onceInThePastIsNothing() throws {
        let once = try schedule(repeat: .once, start: "2026-10-20")
        XCTAssertTrue(Meds.doseAlerts(once, lastTaken: nil, now: at("2026-10-25T12:00:00"), count: 14).isEmpty)
    }

    private func setting(_ key: String, repeat r: ReminderRepeat, dayOfWeek: Int? = nil) throws -> ReminderSetting {
        let json = """
        {"id":"\(UUID().uuidString)","key":"\(key)","repeat":\(r.rawValue),"time":"09:00:00","active":true,
         "notificationId":2001,"dayOfWeek":\(dayOfWeek.map(String.init) ?? "null")}
        """
        return try Wire.decoder.decode(ReminderSetting.self, from: Data(json.utf8))
    }

    func testRoutineAlerts_weeklyOnFixedWeekday() throws {
        let s = try setting("weeklyReview", repeat: .weekly, dayOfWeek: 3)   // Wednesday
        let a = Meds.routineAlerts(s, lastDone: nil, now: at("2026-10-25T12:00:00"), count: 2)
        XCTAssertEqual(a.map(\.fire), ["2026-10-28T09:00:00", "2026-11-04T09:00:00"].map(at))
        XCTAssertEqual(a.first?.title, "Weekly Review")
    }

    func testRoutineAlerts_monthlySkipsWhenDoneWithinInterval() throws {
        let s = try setting("measurement", repeat: .monthly)
        let now = at("2026-10-25T08:00:00")
        XCTAssertEqual(Meds.routineAlerts(s, lastDone: nil, now: now, count: 1).first?.fire, at("2026-10-25T09:00:00"))
        XCTAssertEqual(Meds.routineAlerts(s, lastDone: Day(y: 2026, m: 10, d: 10), now: now, count: 1).first?.fire,
                       at("2026-11-24T09:00:00"), "done this month → next one a month out")
    }

    func testPlan_soonestFirstWithinOSLimit() throws {
        let meds = try (0..<6).map { try schedule("Med \($0)", repeat: .daily, start: "2026-01-01") }
        let plan = Meds.plan(schedules: meds, doses: [], settings: [], lastDone: [:], now: at("2026-10-25T12:00:00"))
        XCTAssertEqual(plan.count, Meds.maxPending)
        XCTAssertEqual(plan.map(\.fire), plan.map(\.fire).sorted())
        XCTAssertEqual(Set(plan.map(\.id)).count, plan.count, "ids are unique")
    }

    func testDoseAlertsCarryWhatTheLogActionNeeds() throws {
        let inj = try schedule("Test C", repeat: .weekly, start: "2026-10-01")   // form 1 = injection; due Thursdays
        let doses = [try dose("Test C", at: "2026-10-22T08:00:00", site: "Left glute")]
        let a = try XCTUnwrap(Meds.plan(schedules: [inj], doses: doses, settings: [], lastDone: [:], now: at("2026-10-25T12:00:00")).first)
        XCTAssertEqual(a.dose, Meds.DoseRef(scheduleId: inj.id, day: Day(y: 2026, m: 10, d: 29), site: "Right glute"))
        XCTAssertEqual(Day(iso: "2026-10-29"), Day(y: 2026, m: 10, d: 29))
    }
}

import Foundation

/// The AI coaching review: your coaching goals (GoalSettings.coachingGoals) as the success criteria,
/// plus a compact trend summary of your data. Medications, labs, cessation, notes and photos are
/// never included — the same exclusions as the other apps' AI features.
enum Coach {
    static let promptKey = "coach_review"

    static let defaultPrompt = """
    You are the user's fitness and recovery coach reviewing their self-tracked data: daily energy and nutrition \
    (last 28 days), training sessions (last 8 weeks), overnight recovery from Apple Watch (last 28 nights), \
    check-ins and body measurements.
    Respond with ONLY a JSON object in this shape:
    {
      "status": "on-track" | "adjust" | "unclear",
      "headline": "<one sentence: the overall verdict>",
      "sections": [{ "title": "<one of the user's coaching priorities, or a short topic>", "points": ["<short, specific point citing the data>"] }],
      "actions": ["<0-3 specific adjustments, only when the data supports them>"]
    }
    Use one section per coaching priority the user lists (in their order), each with 1-3 points. If there are no \
    stated priorities, use: Training, Energy balance, Nutrition, Recovery, Body measurements.
    RULES:
    - The USER'S COACHING GOALS below are the success criteria. Follow their coaching style exactly.
    - Judge multi-day and multi-week TRENDS, never a single day, meal or reading. Today's numbers are partial.
    - Cite real numbers from the data (averages, ranges, changes). Say plainly when data is too sparse to conclude.
    - Days marked "not logged" have no food entries — exclude them from intake and deficit averages; do not treat \
    them as fasting.
    - Calories out on "est" days are an estimate (no Apple Health energy); weigh Health days more.
    - Smart-scale body fat and muscle mass are noisy: only weigh changes sustained over several weeks.
    - Training: judge progression per exercise from weight × reps (or seconds) over time and the difficulty ratings.
    - Recovery: compare HRV and sleeping heart rate with the person's own baseline over several nights, with \
    training load and soreness check-ins.
    - Diet: coach moderation, never elimination. Zero-sugar drinks are not sugary drinks. A small treat is normal.
    - If nothing meaningful needs changing, set status "on-track", say so, and leave actions empty.
    - If a stated goal is clearly unsafe, don't coach toward it — say why and suggest discussing it with a doctor.
    - Plain text inside the JSON strings — no markdown.
    """

    struct Section: Codable, Equatable, Identifiable {
        var id: String { title }
        let title: String
        let points: [String]
    }

    struct Report: Codable, Equatable {
        let generatedAt: Date
        let status: String
        let headline: String
        let sections: [Section]
        let actions: [String]

        static func parse(_ o: [String: Any], at: Date) -> Report? {
            let sections = (o["sections"] as? [[String: Any]] ?? []).compactMap { s -> Section? in
                guard let t = s["title"] as? String else { return nil }
                return Section(title: t, points: (s["points"] as? [String] ?? []).filter { !$0.isEmpty })
            }
            let headline = o["headline"] as? String ?? ""
            guard !headline.isEmpty || !sections.isEmpty else { return nil }
            return Report(generatedAt: at, status: (o["status"] as? String ?? "unclear").lowercased(), headline: headline,
                          sections: sections, actions: (o["actions"] as? [String] ?? []).filter { !$0.isEmpty })
        }
    }

    /// Everything the context is built from (a plain value, so it's testable and built off the main thread).
    struct Inputs {
        var today: Day
        var goals: GoalSettings?
        var coachingGoals: String?
        var meals: [Day: [MealEntry]] = [:]
        var drinks: [Day: [DrinkEntry]] = [:]
        var energy: EnergyHistory
        var sessions: [WorkoutSession] = []
        var routines: [UUID: String] = [:]
        var exercises: [UUID: ExerciseDefinition] = [:]
        var sets: [ExerciseSet] = []
        var feedback: [ExerciseFeedback] = []
        var nights: [NightMetrics] = []
        var readiness: [Day: Int] = [:]
        var checkIns: [RecoveryEntry] = []
        var body: [BodyMeasurement] = []
        /// This morning's readiness (Today check-in only).
        var readinessToday: Readiness?
        /// Today's notes only (Today check-in) — past days' notes are never sent, like the other apps.
        var notesToday: [NoteEntry] = []
    }

    static func prompt(template: String?, _ d: Inputs) -> String {
        var t = (template ?? defaultPrompt) + "\n\n"
        if let g = d.coachingGoals?.trimmingCharacters(in: .whitespacesAndNewlines), !g.isEmpty {
            t += "USER'S COACHING GOALS (verbatim):\n\(g)\n\n"
        }
        return t + context(d)
    }

    static func context(_ d: Inputs) -> String {
        var t = "TODAY: \(d.today.iso) (\(weekday(d.today)))\n"
        if let g = d.goals {
            t += "\nDAILY TARGETS (min-max; rest day / workout day where split):\n"
            if g.proteinMax > 0 { t += "  protein \(g.proteinMin)-\(g.proteinMax) g\n" }
            t += "  calories rest \(g.caloriesRestMin)-\(g.caloriesRestMax), workout \(g.caloriesActiveMin)-\(g.caloriesActiveMax)\n"
            t += "  carbs rest \(g.carbsRestMin)-\(g.carbsRestMax) g, workout \(g.carbsActiveMin)-\(g.carbsActiveMax) g\n"
            if g.fatMax > 0 { t += "  fat \(g.fatMin)-\(g.fatMax) g\n" }
            if g.fiberMax > 0 { t += "  fiber \(g.fiberMin)-\(g.fiberMax) g\n" }
            if let a = g.addedSugarMax, a > 0 { t += "  added sugar under \(a) g\n" }
            let body = [("weight", g.goalWeightLbs, "lb"), ("waist", g.goalWaistInches, "in"), ("body fat", g.goalBodyFatPercent, "%"),
                        ("chest", g.goalChestInches, "in"), ("shoulders", g.goalShouldersInches, "in"), ("arms", g.goalArmsInches, "in"),
                        ("thighs", g.goalThighsInches, "in"), ("calves", g.goalCalvesInches, "in")]
                .compactMap { n, v, u in v.map { "\(n) \(f1($0))\(u == "%" ? "%" : " \(u)")" } }
            if !body.isEmpty { t += "  body goals: \(body.joined(separator: ", "))\n" }
        }
        t += energySection(d) + trainingSection(d) + recoverySection(d) + bodySection(d)
        return t
    }

    // MARK: Today check-in

    /// The other apps' "daily_check" prompt (same key, so a saved override applies here too).
    static let dailyKey = "daily_check"

    static let dailyPrompt = """
    You are a supportive but honest health coach doing a quick check-in on one person's day so far.
    CURRENT TIME OF DAY: {timeOfDay} on {dayOfWeek} (exact time in NOW below). Your framing MUST match it — no \
    start-of-day greeting unless it is actually morning. Judge what's logged UP TO NOW and aim tips at the time that \
    REMAINS today.
    Respond with ONLY a JSON object:
    {
      "tone": "good" | "mixed" | "poor",
      "synopsis": "<2-3 sentences on how today is going and why, against the user's goals and today's day type>",
      "tips": ["<0-3 short, actionable suggestions for the REST of today>"]
    }
    GROUNDING — every claim must trace to a number or entry below; don't invent amounts or times.
    - "not logged" means not recorded, never skipped — don't fault the person for it.
    - Respect TODAY'S DAY TYPE: targets differ for workout and recovery days, and a workout isn't expected on a \
    recovery day.
    - Energy: compare intake with projected calories out and the user's intended deficit; flag a deficit that is \
    becoming unnecessarily large as much as an overshoot. Projected out is partial until the day ends.
    - Recovery: raise a concern only when last night's HRV / sleeping heart rate / sleep is clearly off the person's \
    norm or soreness is high — not for ordinary variation.
    - Use TODAY'S NOTES for how the person feels and what the day holds (travel, busy day, hunger, soreness, \
    energy). Let them shape the advice — e.g. a heavy-legs note on a workout day matters more than a small readiness dip.
    - Use LAST 7 DAYS to tell a one-off from a pattern.
    - Zero-sugar drinks are not sugary drinks. A small treat is normal. Coach moderation, never elimination.
    Be concise and calm. If they're on track, say so and give no tips.
    """

    struct DailyCheck: Codable, Equatable {
        let generatedAt: Date
        let tone: String
        let synopsis: String
        let tips: [String]

        static func parse(_ o: [String: Any], at: Date) -> DailyCheck? {
            guard let s = o["synopsis"] as? String, !s.isEmpty else { return nil }
            return DailyCheck(generatedAt: at, tone: (o["tone"] as? String ?? "mixed").lowercased(), synopsis: s,
                              tips: (o["tips"] as? [String] ?? []).filter { !$0.isEmpty })
        }
    }

    static func dailyPrompt(template: String?, _ d: Inputs, now: Date) -> String {
        let hour = Calendar.current.component(.hour, from: now)
        let part = hour < 5 ? "the middle of the night" : hour < 12 ? "the morning" : hour < 17 ? "the afternoon"
            : hour < 21 ? "the evening" : "late at night"
        var t = (template ?? dailyPrompt).replacingOccurrences(of: "{timeOfDay}", with: part)
            .replacingOccurrences(of: "{dayOfWeek}", with: d.today.date.formatted(.dateTime.weekday(.wide))) + "\n\n"
        if let g = d.coachingGoals?.trimmingCharacters(in: .whitespacesAndNewlines), !g.isEmpty {
            t += "USER'S COACHING GOALS (verbatim) — the success criteria; follow them:\n\(g)\n\n"
        }
        return t + todayContext(d, now: now)
    }

    static func todayContext(_ d: Inputs, now: Date) -> String {
        let day = d.today
        let typeRaw = d.energy.log(day)?.dayType ?? 0
        let active = d.energy.log(day)?.isActiveDay ?? false
        let hm = now.formatted(.dateTime.hour().minute())
        var t = "NOW: \(day.iso) \(weekday(day)) \(hm)\n"
        t += "TODAY'S DAY TYPE: \(dayTypes[min(4, max(0, typeRaw))]) (\(active ? "workout" : "rest/recovery")-day targets apply)\n"
        if let g = d.goals {
            t += "TODAY'S TARGETS: calories \(active ? g.caloriesActiveMin : g.caloriesRestMin)-\(active ? g.caloriesActiveMax : g.caloriesRestMax), "
                + "protein \(g.proteinMin)-\(g.proteinMax) g, carbs \(active ? g.carbsActiveMin : g.carbsRestMin)-\(active ? g.carbsActiveMax : g.carbsRestMax) g"
            if g.fatMax > 0 { t += ", fat \(g.fatMin)-\(g.fatMax) g" }
            if g.fiberMax > 0 { t += ", fiber \(g.fiberMin)-\(g.fiberMax) g" }
            if let a = g.addedSugarMax, a > 0 { t += ", added sugar under \(a) g" }
            if let lo = active ? g.waterActiveMin : g.waterRestMin, let hi = active ? g.waterActiveMax : g.waterRestMax, hi > 0 { t += ", fluids \(lo)-\(hi) oz" }
            t += "\n"
        }
        t += "\nTODAY'S NOTES (the person's own words):\n"
        if d.notesToday.isEmpty { t += "(none)\n" }
        for n in d.notesToday.sorted(by: { $0.time < $1.time }) { t += "  \(n.time.formatted(.dateTime.hour().minute())) \"\(n.text)\"\n" }
        let m = (d.meals[day] ?? []).sorted { $0.time < $1.time }, dr = (d.drinks[day] ?? []).sorted { $0.time < $1.time }
        t += "\nLOGGED TODAY:\n"
        if m.isEmpty && dr.isEmpty { t += "(nothing logged yet)\n" }
        for x in m {
            t += "  \(x.time.formatted(.dateTime.hour().minute())) \(x.mealType.label) \"\(x.description)\"" + (x.calories.map { " \($0) kcal" } ?? "")
                + (x.proteinG.map { ", \(Int($0)) g protein" } ?? "") + "\n"
        }
        for x in dr {
            t += "  \(x.time.formatted(.dateTime.hour().minute())) drink \"\(x.description)\"" + (x.ounces.map { " \(Int($0)) oz" } ?? "")
                + (x.calories.map { ", \($0) kcal" } ?? "") + ((x.addedSugarG ?? 0) > 0 ? ", \(Int(x.addedSugarG!)) g added sugar" : "") + "\n"
        }
        let kin = Nutrition.calories(m, dr)
        t += "TOTALS SO FAR: \(kin) kcal, \(Int(Nutrition.protein(m, dr))) g protein, \(Int(Nutrition.carbs(m, dr))) g carbs, "
            + "\(Int(Nutrition.fat(m, dr))) g fat, \(Int(Nutrition.fiber(m, dr))) g fiber, \(Int(Nutrition.addedSugar(m, dr))) g added sugar, "
            + "\(Nutrition.fluidOz(dr)) oz fluids\n"
        if let out = d.energy.out(day) {
            t += "ENERGY: projected out \(out.kcal) kcal (\(out.live ? "Health activity so far + typical resting" : out.fromHealth ? "Apple Health" : "estimate")) → "
                + "net so far \(kin - out.kcal) kcal\n"
        }
        var nets: [Int] = []
        for i in 1...7 {
            let p = day.adding(-i), pm = d.meals[p] ?? [], pd = d.drinks[p] ?? []
            if !(pm.isEmpty && pd.isEmpty), let o = d.energy.out(p) { nets.append(Nutrition.calories(pm, pd) - o.kcal) }
        }
        if !nets.isEmpty { t += "7-DAY AVERAGE NET: \(nets.reduce(0, +) / nets.count) kcal/day over \(nets.count) logged days\n" }

        t += "\nLAST NIGHT & READINESS:\n"
        if let n = d.nights.first(where: { $0.day == day }) {
            t += "  sleep \(f1(n.sleepHours)) h" + (n.deepRemHours.map { ", deep+REM \(f1($0)) h" } ?? "")
                + (n.hrvMs.map { ", HRV \(Int($0.rounded())) ms" } ?? "") + (n.sleepingHR.map { ", sleeping HR \(Int($0.rounded())) bpm" } ?? "") + "\n"
        } else { t += "  (no sleep data for last night)\n" }
        if let r = d.readinessToday {
            t += "  readiness \(r.score)/100 (\(r.level))" + (r.calibrating ? " — still calibrating" : "") + "\n"
            for f in r.factors { t += "    \(f.name): \(f.detail)\n" }
            for f in r.flags { t += "    flag: \(f)\n" }
        }
        if let c = d.checkIns.first(where: { $0.date == day && !$0.isAuto }) {
            t += "  your check-in: recovery \(c.recoveryRating.map(String.init) ?? "-")/10, fatigue \(c.fatigueRating.map(String.init) ?? "-")/10"
                + (c.sorenessLocations.map { ", sore: \($0) (\(c.sorenessSeverity ?? 0)/10)" } ?? "") + "\n"
        }

        t += "\nWORKOUTS (today and the last 3 days):\n"
        let recent = d.sessions.filter { $0.date >= day.adding(-3) && $0.date <= day }.sorted { $0.date > $1.date }
        if recent.isEmpty { t += "  (none logged)\n" }
        for s in recent {
            let n = s.id.map { id in Set(d.sets.filter { $0.workoutSessionId == id && $0.completed }.map(\.exerciseDefinitionId)).count } ?? 0
            let hard = s.id.map { id in d.feedback.filter { $0.workoutSessionId == id && $0.difficulty.rawValue >= Difficulty.hard.rawValue }.count } ?? 0
            t += "  \(s.date == day ? "today" : "\(s.date.iso) \(weekday(s.date))"): \(s.routineId.flatMap { d.routines[$0] } ?? "Workout")"
                + (s.totalSeconds.map { ", \($0 / 60) min" } ?? "") + ", \(n) exercises" + (hard > 0 ? ", \(hard) rated hard" : "") + "\n"
        }

        t += "\nLAST 7 DAYS (date | day type | kcal in | protein g | net):\n"
        for i in 1...7 {
            let p = day.adding(-i), pm = d.meals[p] ?? [], pd = d.drinks[p] ?? []
            let type = dayTypes[min(4, max(0, d.energy.log(p)?.dayType ?? 0))]
            guard !(pm.isEmpty && pd.isEmpty) else { t += "  \(p.iso) \(weekday(p)) | \(type) | not logged\n"; continue }
            let k = Nutrition.calories(pm, pd)
            t += "  \(p.iso) \(weekday(p)) | \(type) | \(k) | \(Int(Nutrition.protein(pm, pd))) | \(d.energy.out(p).map { String(k - $0.kcal) } ?? "?")\n"
        }
        return t
    }

    private static let dayTypes = ["unset", "workout", "recovery", "active recovery", "high workload"]

    private static func energySection(_ d: Inputs) -> String {
        var t = "\nENERGY & NUTRITION, last 28 days (in = logged food/drink kcal; out = Health resting+active, or est):\n"
        t += "date | day type | in | out | net | protein g | carbs g | fat g | fiber g | added sugar g | fluids oz\n"
        var weekNets: [[Int]] = [[], [], [], []]
        for i in (0..<28).reversed() {
            let day = d.today.adding(-i)
            let m = d.meals[day] ?? [], dr = d.drinks[day] ?? []
            let type = dayTypes[min(4, max(0, d.energy.log(day)?.dayType ?? 0))]
            let label = "\(day.iso) \(weekday(day))\(i == 0 ? " (today, partial)" : "")"
            guard !m.isEmpty || !dr.isEmpty else { t += "\(label) | \(type) | not logged\n"; continue }
            let kin = Nutrition.calories(m, dr)
            let out = d.energy.out(day)
            let outText = out.map { "\($0.kcal)\($0.fromHealth ? "" : " est")" } ?? "?"
            var net = "?"
            if let o = out, i > 0 { net = String(kin - o.kcal); weekNets[(i - 1) / 7].append(kin - o.kcal) }
            t += "\(label) | \(type) | \(kin) | \(outText) | \(net) | \(Int(Nutrition.protein(m, dr))) | \(Int(Nutrition.carbs(m, dr))) | "
                + "\(Int(Nutrition.fat(m, dr))) | \(Int(Nutrition.fiber(m, dr))) | \(Int(Nutrition.addedSugar(m, dr))) | \(Nutrition.fluidOz(dr))\n"
        }
        t += "Average net per completed, logged day (negative = deficit): "
        t += weekNets.enumerated().map { w, nets in
            "\(w == 0 ? "last 7 days" : "\(w * 7 + 1)-\(w * 7 + 7) days ago") " + (nets.isEmpty ? "n/a" : "\(nets.reduce(0, +) / nets.count) (\(nets.count) days)")
        }.joined(separator: "; ") + "\n"
        return t
    }

    private static func trainingSection(_ d: Inputs) -> String {
        let since = d.today.adding(-56)
        let sessions = d.sessions.filter { $0.date >= since && $0.date <= d.today }.sorted { $0.date < $1.date }
        var t = "\nTRAINING, last 8 weeks (\(sessions.count) sessions; weight × reps, or seconds; difficulty in brackets):\n"
        let setsBySession = Dictionary(grouping: d.sets.filter(\.completed)) { $0.workoutSessionId }
        let fbBySession = Dictionary(grouping: d.feedback) { $0.workoutSessionId }
        for s in sessions {
            guard let id = s.id else { continue }
            let mins = s.totalSeconds.map { " \($0 / 60) min" } ?? ""
            t += "\(s.date.iso) \(weekday(s.date)) \(s.routineId.flatMap { d.routines[$0] } ?? "Workout")\(mins)\n"
            let sets = (setsBySession[id] ?? []).sorted { ($0.createdAt ?? .distantPast, $0.setNumber) < ($1.createdAt ?? .distantPast, $1.setNumber) }
            var order: [UUID] = []
            for x in sets where !order.contains(x.exerciseDefinitionId) { order.append(x.exerciseDefinitionId) }
            for ex in order {
                let list = sets.filter { $0.exerciseDefinitionId == ex }.map { x -> String in
                    if let s = x.durationSeconds, x.reps == nil { return "\(s)s" + (x.weight.map { " @\(f1($0))" } ?? "") }
                    return (x.weight.map { "\(f1($0))×" } ?? "") + (x.reps.map(String.init) ?? "?")
                }
                let fb = fbBySession[id]?.first { $0.exerciseDefinitionId == ex }
                var flags = [fb.flatMap { $0.difficulty == .unset ? nil : $0.difficulty.label }].compactMap { $0 }
                if fb?.painOrDiscomfort == true { flags.append("pain") }
                if fb?.formIssues == true { flags.append("form issues") }
                t += "  \(d.exercises[ex]?.name ?? "Exercise"): \(list.joined(separator: ", "))\(flags.isEmpty ? "" : " [\(flags.joined(separator: ", "))]")\n"
            }
        }
        return t
    }

    private static func recoverySection(_ d: Inputs) -> String {
        let since = d.today.adding(-28)
        var t = "\nOVERNIGHT RECOVERY, last 28 nights (Apple Watch, asleep only; readiness 0-100 is the app's morning score):\n"
        t += "wake date | sleep h | deep+REM h | HRV ms | sleeping HR bpm | readiness\n"
        let nights = d.nights.filter { $0.day >= since }.sorted { $0.day < $1.day }
        if nights.isEmpty { t += "(no Health sleep data)\n" }
        for n in nights {
            t += "\(n.day.iso) | \(f1(n.sleepHours)) | \(n.deepRemHours.map(f1) ?? "-") | \(n.hrvMs.map { String(Int($0.rounded())) } ?? "-") | "
                + "\(n.sleepingHR.map { String(Int($0.rounded())) } ?? "-") | \(d.readiness[n.day].map(String.init) ?? "-")\n"
        }
        let checks = d.checkIns.filter { $0.date >= since && !$0.isAuto }.sorted { $0.date < $1.date }
        if !checks.isEmpty {
            t += "Check-ins you made yourself (recovery and fatigue 1-10, soreness severity 0-10):\n"
            for c in checks {
                t += "\(c.date.iso) recovery \(c.recoveryRating.map(String.init) ?? "-"), fatigue \(c.fatigueRating.map(String.init) ?? "-")"
                    + (c.sorenessLocations.map { ", sore: \($0) (\(c.sorenessSeverity ?? 0))" } ?? "") + "\n"
            }
        }
        return t
    }

    private static func bodySection(_ d: Inputs) -> String {
        let rows = d.body.filter { $0.date >= d.today.adding(-112) }.sorted { $0.date < $1.date }
        var t = "\nBODY MEASUREMENTS, last 16 weeks:\n"
        if rows.isEmpty { return t + "(none)\n" }
        for m in rows {
            let bits = [("weight", m.weightLbs), ("waist", m.waistInches), ("body fat %", m.bodyFatPercent), ("muscle lb", m.muscleMassLbs),
                        ("chest", m.chestInches), ("shoulders", m.shouldersInches), ("arms", m.armsInches), ("forearms", m.forearmsInches),
                        ("thighs", m.thighsInches), ("calves", m.calvesInches), ("hips", m.hipsInches)]
                .compactMap { n, v in v.map { "\(n) \(f1($0))" } }
            if !bits.isEmpty { t += "\(m.date.iso): \(bits.joined(separator: ", "))\n" }
        }
        return t
    }

    private static func weekday(_ d: Day) -> String { d.date.formatted(.dateTime.weekday(.abbreviated)) }
    private static func f1(_ v: Double) -> String { v.formatted(.number.precision(.fractionLength(0...1)).grouping(.never)) }
}

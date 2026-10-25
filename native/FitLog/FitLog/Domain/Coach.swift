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

    /// Its own prompt key: the other apps' "daily_check" template has a different reply shape (no concerns).
    static let dailyKey = "coach_today"

    static let dailyPrompt = """
    You are a supportive but honest health coach doing a check-in on one person's UNFINISHED day.
    It is {timeOfDay} on {dayOfWeek} (exact time in NOW). Frame everything for the time that REMAINS today — no \
    start-of-day greeting unless it is actually morning.
    Respond with ONLY a JSON object:
    {
      "synopsis": "<2-3 sentences: how today is going so far, judged against PACE (below), not against full-day totals>",
      "concerns": [{ "kind": "calories_high" | "calories_low" | "protein_low" | "recovery" | "hydration" | "other",
                     "severity": "minor" | "major", "evidence": "<the specific number or note that shows it>" }],
      "tips": ["<0-3 specific suggestions for the rest of today, using the REMAINING amounts below when useful>"]
    }
    HOW TO JUDGE AN UNFINISHED DAY — the app has already worked out PACE for you; trust it:
    - Intake so far is NOT the day's total. Compare it with "expected by now" (from the person's own usual eating \
    pattern), never with the full-day target. Being under the full-day target before dinner is normal progress, not a \
    concern.
    - Calories: the budget is projected full-day expenditure minus the intended deficit. The fixed calorie range is \
    reference only. Report a calories concern ONLY if PACE says over budget (calories_high) or deficit-too-large \
    (calories_low).
    - Protein: report the grams REMAINING to the minimum. Only add protein_low when PACE says target-unlikely.
    - Recovery: weigh HRV, sleeping heart rate, sleep and soreness against the person's norm over several nights, \
    training load and the day type. One reading a little off is NOT a concern; add "recovery" only for a clear, \
    multi-signal or sustained change, or a note/check-in that says so. A workout isn't expected on a recovery day.
    - "not logged" means not recorded — never skipped or missed.
    - Use TODAY'S NOTES for how the person feels and what the day holds; a note can justify a concern ("other").
    - Zero-sugar drinks are not sugary drinks. A small treat is normal. Coach moderation, never elimination.
    - If nothing is actually wrong, concerns is [] and tips is [] or one light pointer (e.g. the protein left) — \
    don't invent problems or generic advice ("eat more", "stay hydrated").
    Every claim must trace to a number or entry below.
    """

    struct Concern: Codable, Equatable {
        let kind: String
        let severity: String
        let evidence: String
    }

    struct DailyCheck: Codable, Equatable {
        let generatedAt: Date
        /// Derived in code from the vetted concerns: good (on track), mixed, or poor.
        let tone: String
        let synopsis: String
        let tips: [String]
        var concerns: [Concern]? = nil
        /// "≈ 1,050–1,150 kcal and 85 g protein left today" — worked out in code.
        var remaining: String? = nil

        /// Parse the reply and decide the status from concerns the data supports: intake concerns the
        /// pace doesn't back up (e.g. "low calories" at 1:50 PM with dinner to come) are dropped.
        static func parse(_ o: [String: Any], at: Date, pace: DayPace? = nil) -> DailyCheck? {
            guard let s = o["synopsis"] as? String, !s.isEmpty else { return nil }
            let raw = (o["concerns"] as? [[String: Any]] ?? []).map {
                Concern(kind: ($0["kind"] as? String ?? "other").lowercased(), severity: ($0["severity"] as? String ?? "minor").lowercased(),
                        evidence: $0["evidence"] as? String ?? "")
            }
            let kept = raw.filter { supported($0, pace) }
            return DailyCheck(generatedAt: at, tone: status(kept), synopsis: s,
                              tips: (o["tips"] as? [String] ?? []).filter { !$0.isEmpty }, concerns: kept, remaining: pace.flatMap(remainingText))
        }

        static func supported(_ c: Concern, _ pace: DayPace?) -> Bool {
            guard let p = pace else { return true }
            switch c.kind {
            case "calories_high": return p.overBudget
            case "calories_low": return p.deficitTooLarge
            case "protein_low": return p.proteinUnlikely
            default: return true
            }
        }

        static func status(_ concerns: [Concern]) -> String {
            concerns.isEmpty ? "good" : concerns.contains { $0.severity == "major" } ? "poor" : "mixed"
        }

        static func remainingText(_ p: DayPace) -> String? {
            var bits: [String] = []
            if let c = p.caloriesRemaining {
                bits.append(c.upperBound == 0 ? "calorie budget used" : "\(c.lowerBound == c.upperBound ? "" : "\(c.lowerBound.formatted())–")\(c.upperBound.formatted()) kcal")
            }
            if let g = p.proteinRemaining { bits.append(g == 0 ? "protein target met" : "\(g) g protein") }
            return bits.isEmpty ? nil : "Left today: " + bits.joined(separator: " · ")
        }
    }

    static func dailyPrompt(template: String?, _ d: Inputs, now: Date, pace: DayPace) -> String {
        let hour = Calendar.current.component(.hour, from: now)
        let part = hour < 5 ? "the middle of the night" : hour < 12 ? "the morning" : hour < 17 ? "the afternoon"
            : hour < 21 ? "the evening" : "late at night"
        var t = (template ?? dailyPrompt).replacingOccurrences(of: "{timeOfDay}", with: part)
            .replacingOccurrences(of: "{dayOfWeek}", with: d.today.date.formatted(.dateTime.weekday(.wide))) + "\n\n"
        if let g = d.coachingGoals?.trimmingCharacters(in: .whitespacesAndNewlines), !g.isEmpty {
            t += "USER'S COACHING GOALS (verbatim) — the success criteria; follow them:\n\(g)\n\n"
        }
        return t + todayContext(d, now: now, pace: pace)
    }

    static func paceSection(_ p: DayPace) -> String {
        var t = "\nPACE (worked out by the app — use these, don't recompute):\n"
        t += "  usual share of the day's food eaten by now: \(Int((p.eatenShare * 100).rounded()))% "
            + (p.historyDays > 0 ? "(median of your last \(p.historyDays) fully logged days)" : "(default 7 AM–9 PM spread; not enough history)") + "\n"
        t += "  calories so far \(p.caloriesIn)" + (p.expectedCaloriesByNow.map { ", expected by now ≈ \($0)" } ?? "") + "\n"
        if let out = p.projectedOut, let b = p.calorieBudget {
            t += "  projected full-day calories out \(out) (\(p.outBasis))\n"
            t += "  intended deficit \(p.deficit.lowerBound)-\(p.deficit.upperBound) kcal → end-of-day intake budget \(b.lowerBound)-\(b.upperBound) kcal\n"
            if let r = p.caloriesRemaining { t += "  calories remaining to land in budget: \(r.lowerBound)-\(r.upperBound)\n" }
        } else {
            t += "  projected calories out: unknown (no Health or body data) — judge calories only loosely\n"
        }
        if let f = p.fixedCalories { t += "  fixed calorie range for today's day type \(f.lowerBound)-\(f.upperBound) (reference only)\n" }
        if let pt = p.proteinTarget {
            t += "  protein so far \(Int(p.proteinIn.rounded())) g" + (p.expectedProteinByNow.map { ", expected by now ≈ \($0) g" } ?? "")
                + ", target \(pt.lowerBound)-\(pt.upperBound) g, remaining to minimum \(p.proteinRemaining ?? 0) g\n"
        }
        let flags = [p.overBudget ? "over-budget" : nil, p.deficitTooLarge ? "deficit-too-large" : nil, p.proteinUnlikely ? "protein-target-unlikely" : nil]
            .compactMap { $0 }
        t += "  flags: \(flags.isEmpty ? "none — intake is normal progress for an unfinished day" : flags.joined(separator: ", "))\n"
        return t
    }

    static func todayContext(_ d: Inputs, now: Date, pace: DayPace) -> String {
        let day = d.today
        let typeRaw = d.energy.log(day)?.dayType ?? 0
        let active = d.energy.log(day)?.isActiveDay ?? false
        var t = "NOW: \(day.iso) \(weekday(day)) \(now.formatted(.dateTime.hour().minute()))\n"
        t += "TODAY'S DAY TYPE: \(dayTypes[min(4, max(0, typeRaw))]) (\(active ? "workout" : "rest/recovery")-day targets apply)\n"
        if let g = d.goals {
            var other = ["carbs \(active ? g.carbsActiveMin : g.carbsRestMin)-\(active ? g.carbsActiveMax : g.carbsRestMax) g"]
            if g.fatMax > 0 { other.append("fat \(g.fatMin)-\(g.fatMax) g") }
            if g.fiberMax > 0 { other.append("fiber \(g.fiberMin)-\(g.fiberMax) g") }
            if let a = g.addedSugarMax, a > 0 { other.append("added sugar under \(a) g") }
            if let lo = active ? g.waterActiveMin : g.waterRestMin, let hi = active ? g.waterActiveMax : g.waterRestMax, hi > 0 { other.append("fluids \(lo)-\(hi) oz") }
            t += "OTHER FULL-DAY TARGETS: \(other.joined(separator: ", "))\n"
        }
        t += paceSection(pace)
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
        t += "TOTALS SO FAR: \(pace.caloriesIn) kcal, \(Int(Nutrition.protein(m, dr))) g protein, \(Int(Nutrition.carbs(m, dr))) g carbs, "
            + "\(Int(Nutrition.fat(m, dr))) g fat, \(Int(Nutrition.fiber(m, dr))) g fiber, \(Int(Nutrition.addedSugar(m, dr))) g added sugar, "
            + "\(Nutrition.fluidOz(dr)) oz fluids\n"
        var nets: [Int] = []
        for i in 1...7 {
            let p = day.adding(-i), pm = d.meals[p] ?? [], pd = d.drinks[p] ?? []
            if !(pm.isEmpty && pd.isEmpty), let o = d.energy.out(p) { nets.append(Nutrition.calories(pm, pd) - o.kcal) }
        }
        if !nets.isEmpty { t += "7-DAY AVERAGE NET (completed days): \(nets.reduce(0, +) / nets.count) kcal/day over \(nets.count) logged days\n" }

        t += "\nLAST NIGHT & READINESS (norms are the person's own recent nights):\n"
        if let n = d.nights.first(where: { $0.day == day }) {
            t += "  sleep \(f1(n.sleepHours)) h" + (n.deepRemHours.map { ", deep+REM \(f1($0)) h" } ?? "")
                + (n.hrvMs.map { ", HRV \(Int($0.rounded())) ms" } ?? "") + (n.sleepingHR.map { ", sleeping HR \(Int($0.rounded())) bpm" } ?? "") + "\n"
        } else { t += "  (no sleep data for last night)\n" }
        let prior = d.nights.filter { $0.day < day && $0.day >= day.adding(-3) }.sorted { $0.day > $1.day }
        for n in prior {
            t += "  \(n.day.iso): sleep \(f1(n.sleepHours)) h" + (n.hrvMs.map { ", HRV \(Int($0.rounded())) ms" } ?? "")
                + (n.sleepingHR.map { ", sleeping HR \(Int($0.rounded())) bpm" } ?? "") + "\n"
        }
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

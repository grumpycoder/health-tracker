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
      "concerns": [{ "kind": "calories_high" | "calories_low" | "protein_low" | "hydration" | "recovery" | "other",
                     "severity": "watch" | "concern" | "major", "evidence": "<the specific number or note that shows it>" }],
      "tips": [{ "text": "<one specific suggestion for the rest of today>",
                 "area": "calories" | "protein" | "hydration" | "recovery" | "training" | "other",
                 "priority": "high" | "normal" }]
    }
    STATUS — the app shows "On track" unless there is a "concern" or "major". So:
    - "watch" = worth keeping an eye on, no action needed today (e.g. HRV a bit under norm for one night).
    - "concern" = a real problem that calls for action today. "major" = serious.
    - Normal progress through an unfinished day is NOT a concern. If nothing calls for action, use only "watch" \
    items or none, and write the synopsis as on track. The synopsis must agree with the severities you choose.
    HOW TO JUDGE AN UNFINISHED DAY — the app has already worked out PACE for you; trust it:
    - Intake so far is NOT the day's total. Compare it with "expected by now" (from the person's own usual pattern), \
    never with the full-day target.
    - Calories: the budget is projected full-day expenditure minus the intended deficit; the fixed range is reference \
    only. calories_high only if PACE flags over-budget; calories_low only if it flags deficit-too-large.
    - Protein and fluids: give the amount still to go. protein_low / hydration only if PACE flags them.
    - Recovery: weigh HRV, sleeping heart rate, sleep and soreness against the person's norm over several nights, \
    training load and the day type. Use "concern" only when PACE lists recovery evidence or a note/check-in says so; \
    otherwise at most "watch". A workout isn't expected on a recovery day.
    - Use TODAY'S NOTES for how the person feels and what the day holds; a note can justify a concern ("other").
    - "not logged" means not recorded — never skipped or missed.
    WORDING — an unfinished target is not done: never call it "secured", "locked in", "guaranteed" or "hit" until \
    the amount so far actually reaches it. Say "on pace", "within reach", or "N g to go".
    TIPS — proportional and ordered by importance:
    - "high" only for a tip that addresses a "concern" or "major". Everything else is "normal".
    - Put the most useful tip first. Don't tip on an area PACE shows on or ahead of pace (e.g. fluids ahead of \
    expected), unless it's needed to keep a concern from developing.
    - With no concerns, give at most 2 tips — or none — that help the rest of the day (e.g. how to fit the protein \
    still to go into dinner). Never manufacture problems or generic advice ("eat more", "stay hydrated").
    - Zero-sugar drinks are not sugary drinks. A small treat is normal. Coach moderation, never elimination.
    Every claim must trace to a number or entry below.
    """

    struct Concern: Codable, Equatable {
        let kind: String
        /// watch (no effect on status), concern, or major.
        let severity: String
        let evidence: String
    }

    struct DailyCheck: Codable, Equatable {
        let generatedAt: Date
        /// Derived in code from the vetted concerns: good (on track), mixed, or poor.
        let tone: String
        let synopsis: String
        /// Most important first.
        let tips: [String]
        var concerns: [Concern]? = nil
        /// "Left today: 1,300–1,400 kcal · 100 g protein" — worked out in code.
        var remaining: String? = nil

        /// Things to keep an eye on that don't change the status.
        var watching: [Concern] { (concerns ?? []).filter { $0.severity == "watch" } }

        /// Parse the reply, vet each concern against the data, decide the status from what survives, and
        /// order and cap the tips by importance.
        static func parse(_ o: [String: Any], at: Date, pace: DayPace? = nil) -> DailyCheck? {
            guard let s = o["synopsis"] as? String, !s.isEmpty else { return nil }
            let raw = (o["concerns"] as? [[String: Any]] ?? []).map {
                Concern(kind: ($0["kind"] as? String ?? "other").lowercased(), severity: severity($0["severity"] as? String),
                        evidence: $0["evidence"] as? String ?? "")
            }
            let kept = raw.compactMap { vet($0, pace) }
            return DailyCheck(generatedAt: at, tone: status(kept), synopsis: s, tips: rankTips(o["tips"], concerns: kept, pace: pace),
                              concerns: kept, remaining: pace.flatMap(remainingText))
        }

        /// Older replies used minor/major.
        static func severity(_ s: String?) -> String {
            switch (s ?? "").lowercased() { case "major": "major"; case "watch": "watch"; default: "concern" }
        }

        /// Intake concerns the pace doesn't support are dropped; a recovery concern without evidence in the
        /// data (readiness, flags, a factor well off norm, a check-in) is kept only as "watch".
        static func vet(_ c: Concern, _ pace: DayPace?) -> Concern? {
            guard let p = pace else { return c }
            switch c.kind {
            case "calories_high": return p.overBudget ? c : nil
            case "calories_low": return p.deficitTooLarge ? c : nil
            case "protein_low": return p.proteinUnlikely ? c : nil
            case "hydration": return p.hydrationLow ? c : nil
            case "recovery" where c.severity != "watch" && p.recoveryEvidence.isEmpty:
                return Concern(kind: c.kind, severity: "watch", evidence: c.evidence)
            default: return c
            }
        }

        static func status(_ concerns: [Concern]) -> String {
            if concerns.contains(where: { $0.severity == "major" }) { return "poor" }
            return concerns.contains { $0.severity == "concern" } ? "mixed" : "good"
        }

        /// Tips for a real concern first, then the rest in the model's order; tips about an area that's
        /// already well ahead go last. At most 2 when nothing needs action, 3 otherwise.
        static func rankTips(_ value: Any?, concerns: [Concern], pace: DayPace?) -> [String] {
            let urgentAreas = Set(concerns.filter { $0.severity != "watch" }.map { area(ofConcern: $0.kind) })
            let tips: [(text: String, rank: Int, i: Int)] = ((value as? [Any]) ?? []).enumerated().compactMap { i, t in
                let text: String, area: String, high: Bool
                if let s = t as? String { text = s; area = "other"; high = false }
                else if let o = t as? [String: Any], let s = o["text"] as? String {
                    text = s; area = (o["area"] as? String ?? "other").lowercased(); high = (o["priority"] as? String)?.lowercased() == "high"
                } else { return nil }
                guard !text.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
                let rank = urgentAreas.contains(area) ? 0              // fixes a real concern
                    : high && !urgentAreas.isEmpty ? 1                 // the model's call, when something is wrong
                    : pace?.wellAhead(area) == true ? 3                // an area already ahead or done
                    : 2
                return (text, rank, i)
            }
            let cap = urgentAreas.isEmpty ? 2 : 3
            return tips.sorted { ($0.rank, $0.i) < ($1.rank, $1.i) }.prefix(cap).map(\.text)
        }

        static func area(ofConcern kind: String) -> String {
            switch kind {
            case "calories_high", "calories_low": "calories"
            case "protein_low": "protein"
            default: kind
            }
        }

        static func remainingText(_ p: DayPace) -> String? {
            var bits: [String] = []
            if let c = p.caloriesRemaining {
                bits.append(c.upperBound == 0 ? "calorie budget used" : "\(c.lowerBound == c.upperBound ? "" : "\(c.lowerBound.formatted())–")\(c.upperBound.formatted()) kcal")
            }
            if let g = p.proteinRemaining { bits.append(g == 0 ? "protein target met" : "\(g) g protein") }
            if let f = p.fluidsRemaining, f > 0 { bits.append("\(f) oz fluids") }
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
        if let ft = p.fluidTarget {
            t += "  fluids so far \(p.fluidsIn) oz" + (p.expectedFluidsByNow.map { ", expected by now ≈ \($0) oz" } ?? "")
                + ", target \(ft.lowerBound)-\(ft.upperBound) oz, remaining to minimum \(p.fluidsRemaining ?? 0) oz"
                + (p.fluidsIn >= (p.expectedFluidsByNow ?? .max) ? " (on or ahead of pace)" : "") + "\n"
        }
        t += "  recovery evidence: \(p.recoveryEvidence.isEmpty ? "none beyond ordinary variation" : p.recoveryEvidence.joined(separator: "; "))\n"
        let flags = [p.overBudget ? "over-budget" : nil, p.deficitTooLarge ? "deficit-too-large" : nil, p.proteinUnlikely ? "protein-target-unlikely" : nil,
                     p.hydrationLow ? "fluids-short" : nil]
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

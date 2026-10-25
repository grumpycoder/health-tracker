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

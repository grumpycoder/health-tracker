using System.Text;
using System.Text.Json;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Services;

/// <summary>
/// AI analysis of workouts, meals/drinks, sleep, and body-measurement numbers
/// via the Gemini API (free AI Studio tier; the user supplies their own key in
/// Settings). Medications, labs, photos, and freeform measurement text are
/// deliberately NEVER sent. TODAY's notes go to the daily check-in only
/// (user-requested, so it can judge circumstances); the 8-week analysis never
/// sees notes. Cessation (substance) data is sent ONLY when the user explicitly
/// opts in via <see cref="IncludeCessationPrefKey"/>.
/// </summary>
public static class GeminiAnalyzer
{
    /// <summary>Preferences key for the explicit cessation-data opt-in (default off).</summary>
    public const string IncludeCessationPrefKey = "ai_include_cessation";

    /// <summary>Preferences key for the user's own coaching goals/intentions —
    /// included in every prompt as the success criteria, so the AI coaches toward
    /// the user's chosen targets instead of implicit ideals (e.g. "sweet tea to
    /// zero" when the user is deliberately maintaining 16oz/day).</summary>
    public const string UserGoalsPrefKey = "ai_user_goals";

    private static void AppendUserGoals(StringBuilder sb)
    {
        var goals = Microsoft.Maui.Storage.Preferences.Default.Get<string?>(UserGoalsPrefKey, null);
        if (string.IsNullOrWhiteSpace(goals)) return;
        sb.AppendLine("USER'S STATED GOALS & PREFERENCES — these are the success criteria. Coach adherence " +
                      "to THEM; never push toward an implicit ideal (like zero) the user hasn't chosen. " +
                      "EXCEPTION: if a goal is clearly unhealthy or unsafe (crash dieting, dangerous targets, " +
                      "overtraining through pain), do NOT coach toward it — say plainly why, and suggest " +
                      "discussing it with a doctor where appropriate:");
        foreach (var line in goals.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            sb.AppendLine($"  - {line}");
    }
    private const string Model = "gemini-2.5-flash";
    private const int WindowDays = 56; // 8 weeks

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Stores the API key (Keychain when available, Preferences otherwise).</summary>
    public static class KeyStore
    {
        private const string Name = "gemini_api_key";

        public static async Task<string?> GetAsync()
        {
            try
            {
                var v = await Microsoft.Maui.Storage.SecureStorage.Default.GetAsync(Name);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { /* SecureStorage unavailable (e.g. unsigned Catalyst dev build) */ }
            var p = Microsoft.Maui.Storage.Preferences.Default.Get<string?>(Name, null);
            return string.IsNullOrEmpty(p) ? null : p;
        }

        public static async Task SetAsync(string value)
        {
            try { await Microsoft.Maui.Storage.SecureStorage.Default.SetAsync(Name, value); return; }
            catch { }
            Microsoft.Maui.Storage.Preferences.Default.Set(Name, value);
        }
    }

    /// <summary>Builds the analysis prompt from the last 8 weeks of
    /// workouts, meals, drinks, and sleep only.</summary>
    public static async Task<string> BuildPromptAsync(AppDbContext db)
    {
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sinceDt = since.ToDateTime(TimeOnly.MinValue);
        var sb = new StringBuilder();

        sb.AppendLine("You are a concise fitness and recovery coach analyzing one person's self-tracked logs (last 8 weeks).");
        sb.AppendLine("Respond with ONLY a JSON object in this shape:");
        sb.AppendLine("""
{
  "analysis": "plain-text analysis with sections: WORKOUT PROGRESSION, BODY TREND, MEAL PATTERNS, SLEEP, TOP 3 ACTIONS. Short uppercase headings and dash bullets, no markdown symbols.",
  "exercises": [{ "name": "<exercise name exactly as it appears in the data>", "action": "progress" | "hold" | "backoff", "target": "<next-week target, e.g. 3x22 reps or 3x35s>" }],
  "topActions": ["<highest-impact action>", "<second>", "<third>"],
  "mealFlags": ["<0-3 short flags about eating/drinking habits worth attention, e.g. 'Sweet tea down from ~32oz to ~16oz/day — keep tapering'>"],
  "bodyTrend": { "status": "on-track" | "off-track" | "unclear", "note": "<one short sentence, e.g. 'Weight down ~1 lb/week'>" }
}
""");
        sb.AppendLine("Be specific and reference the data. Say plainly where data is too sparse to conclude anything.");
        sb.AppendLine("For drinks and treats, judge QUANTITY and TREND — not just how often they appear. Use the weekly " +
                      "volume data: something consumed daily but at half the previous volume is meaningful progress; " +
                      "acknowledge the reduction and suggest the next moderation step rather than blanket elimination.");
        sb.AppendLine("Judge the FOOD, not the venue: a grilled chicken sandwich from a drive-thru is a reasonable " +
                      "protein choice — don't penalize restaurant/fast-food meals as a category; assess what was actually eaten.");
        sb.AppendLine("Zero-sugar drinks (Coke Zero, diet soda, sugar-free) are NOT sugary drinks — treat them as " +
                      "taste variety, not a concern.");
        AppendUserGoals(sb);
        sb.AppendLine();

        sb.AppendLine("WORKOUTS:");
        var sessions = await db.WorkoutSessions
            .Where(s => s.Date >= since)
            .Include(s => s.Routine)
            .Include(s => s.Sets).ThenInclude(x => x.ExerciseDefinition)
            .Include(s => s.Feedback).ThenInclude(f => f.ExerciseDefinition)
            .OrderBy(s => s.Date).ToListAsync();
        if (sessions.Count == 0) sb.AppendLine("(none)");
        foreach (var s in sessions)
        {
            sb.AppendLine($"{s.Date:yyyy-MM-dd} {s.Routine?.Name ?? "Workout"} {(s.TotalSeconds ?? 0) / 60}min" +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));
            foreach (var g in s.Sets.GroupBy(x => x.ExerciseDefinition?.Name ?? "?"))
            {
                var fb = s.Feedback.FirstOrDefault(f => f.ExerciseDefinition?.Name == g.Key);
                var vals = string.Join(",", g.OrderBy(x => x.SetNumber)
                    .Select(x => x.DurationSeconds is { } t ? $"{t}s" : x.Reps?.ToString() ?? "-"));
                var done = g.Count(x => x.Completed);
                sb.AppendLine($"  {g.Key}: {vals} ({done}/{g.Count()} sets done)" +
                              (fb is null || fb.Difficulty == Difficulty.Unset ? "" : $" rated:{fb.Difficulty}") +
                              (string.IsNullOrWhiteSpace(fb?.Comment) ? "" : $" comment:\"{fb!.Comment}\""));
            }
        }

        sb.AppendLine();
        sb.AppendLine("MEALS (date time type description [portion] [tags] [satiety]):");
        var meals = await db.MealEntries.Where(m => m.Time >= sinceDt).OrderBy(m => m.Time).ToListAsync();
        if (meals.Count == 0) sb.AppendLine("(none)");
        foreach (var m in meals)
            sb.AppendLine($"{m.Time:yyyy-MM-dd HH:mm} {m.MealType} \"{m.Description}\"" +
                          (string.IsNullOrWhiteSpace(m.PortionNote) ? "" : $" portion:\"{m.PortionNote}\"") +
                          (m.TagList.Count == 0 ? "" : $" tags:{string.Join("/", m.TagList)}") +
                          (m.Satiety == Satiety.Unset ? "" : $" satiety:{m.Satiety}"));

        sb.AppendLine();
        sb.AppendLine("DRINKS:");
        var drinks = await db.DrinkEntries.Where(d => d.Time >= sinceDt).OrderBy(d => d.Time).ToListAsync();
        if (drinks.Count == 0) sb.AppendLine("(none)");
        foreach (var d in drinks)
            sb.AppendLine($"{d.Time:yyyy-MM-dd HH:mm} \"{d.Description}\"" +
                          (d.Ounces is { } oz ? $" {oz:0.#}oz" : "") +
                          (d.SugarCount is { } su ? $" sugar:{su}" : ""));

        // Pre-aggregated so volume trends (e.g. tapering sweet tea) are unmissable.
        var withOz = drinks.Where(d => d.Ounces is not null && !string.IsNullOrWhiteSpace(d.Description)).ToList();
        if (withOz.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DRINK VOLUME BY WEEK (total oz, week 1 = oldest):");
            foreach (var g in withOz.GroupBy(d => d.Description.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var weeks = g.GroupBy(d => (DateOnly.FromDateTime(d.Time).DayNumber - since.DayNumber) / 7)
                    .OrderBy(w => w.Key)
                    .Select(w => $"wk{w.Key + 1}:{w.Sum(x => x.Ounces ?? 0):0}oz");
                sb.AppendLine($"- {g.Key}: {string.Join(" ", weeks)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("BODY MEASUREMENTS (numbers only):");
        var measurements = await db.BodyMeasurements.Where(m => m.Date >= since).OrderBy(m => m.Date).ToListAsync();
        if (measurements.Count == 0) sb.AppendLine("(none)");
        foreach (var m in measurements)
            sb.AppendLine($"{m.Date:yyyy-MM-dd}" +
                          (m.WeightLbs is { } w ? $" weight:{w:0.#}lbs" : "") +
                          (m.WaistInches is { } wa ? $" waist:{wa:0.##}in" : "") +
                          (m.ChestInches is { } c ? $" chest:{c:0.##}in" : "") +
                          (m.ArmsInches is { } a ? $" arms:{a:0.##}in" : "") +
                          (m.ThighsInches is { } t ? $" thighs:{t:0.##}in" : ""));

        sb.AppendLine();
        sb.AppendLine("SLEEP:");
        var sleep = await db.SleepEntries.Where(s => s.Date >= since).OrderBy(s => s.Date).ToListAsync();
        if (sleep.Count == 0) sb.AppendLine("(none)");
        foreach (var s in sleep)
            sb.AppendLine($"{s.Date:yyyy-MM-dd}" +
                          (s.DurationHours is { } h ? $" {h:0.#}h" : "") +
                          (s.SleepScore is { } sc ? $" score:{sc}" : "") +
                          (s.Interruptions is { } i ? $" interruptions:{i}" : "") +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));

        return sb.ToString();
    }

    public sealed record DailyCheck(string Tone, string Synopsis, List<string> Tips);

    /// <summary>Builds the "how am I doing today" prompt from TODAY's data only
    /// (meals/drinks, last night's sleep, workout, workload, plan — never meds/labs/notes).</summary>
    public static async Task<string> BuildDailyPromptAsync(AppDbContext db)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var dayStart = today.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var sb = new StringBuilder();

        sb.AppendLine("You are a supportive but honest health coach doing a quick mid-day check-in on one person's self-tracked day.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""
{
  "tone": "good" | "mixed" | "poor",
  "synopsis": "<2-3 sentences on how the day is going so far and the likely reasons — e.g. possible bloating from high-sodium restaurant food, sugary drinks adding up, short sleep dragging energy, solid workout done. Encouraging when earned, direct when not.>",
  "tips": ["<up to 3 short, actionable suggestions for the REST of today>"]
}
""");
        sb.AppendLine("Consider meal quality/timing, sugary drinks, sleep duration and score, whether a workout happened on a workout day, and physical workload — but JUDGE IN CONTEXT:");
        sb.AppendLine("- Use TODAY'S NOTES for circumstances (travel, events, busy days). A fast-food dinner on a day spent out running errands is life, not failure.");
        sb.AppendLine("- Use LAST 7 DAYS to tell one-off indulgences from patterns. A single off-plan meal in an otherwise solid stretch gets a light touch ('enjoy it, back to normal tomorrow'); direct warnings are for things repeating across several days.");
        sb.AppendLine("- Judge the FOOD, not the venue. A grilled chicken sandwich from a drive-thru is a reasonable protein choice, not a lapse; a burger-and-fries combo is different. Don't penalize 'restaurant/fast food' as a category — eating-out sodium is worth one mention only when frequent.");
        sb.AppendLine("- Zero-sugar drinks (Coke Zero, diet soda, sugar-free) are NOT sugary drinks — taste variety, not a concern.");
        AppendUserGoals(sb);
        sb.AppendLine("If little is logged yet, say so and suggest what to log.");
        sb.AppendLine();
        sb.AppendLine($"NOW: {now:yyyy-MM-dd HH:mm} ({now.DayOfWeek})");

        var day = await db.DailyLogs.FirstOrDefaultAsync(x => x.Date == today);
        sb.AppendLine($"PLANNED DAY TYPE: {(day is null || day.DayType == DayType.Unset ? "(not set)" : day.DayType.ToString())}");

        // Today's notes carry the circumstances (travel, events, errands) — the
        // single biggest tone corrector for the check-in.
        sb.AppendLine("TODAY'S NOTES:");
        var notes = await db.NoteEntries.Where(n => n.Time >= dayStart && n.Time < dayEnd)
            .OrderBy(n => n.Time).ToListAsync();
        if (notes.Count == 0) sb.AppendLine("(none)");
        foreach (var n in notes)
            sb.AppendLine($"  {n.Time:HH:mm} \"{n.Text}\"");

        var sleep = await db.SleepEntries.FirstOrDefaultAsync(s => s.Date == today);
        sb.AppendLine("SLEEP (last night): " + (sleep is null
            ? "(not logged)"
            : $"{sleep.DurationHours:0.#}h score:{sleep.SleepScore} interruptions:{sleep.Interruptions}" +
              (string.IsNullOrWhiteSpace(sleep.Notes) ? "" : $" note:\"{sleep.Notes}\"")));

        sb.AppendLine("WORKOUT TODAY:");
        var sessions = await db.WorkoutSessions.Where(s => s.Date == today)
            .Include(s => s.Sets).ThenInclude(x => x.ExerciseDefinition).ToListAsync();
        if (sessions.Count == 0) sb.AppendLine("(none yet)");
        foreach (var s in sessions)
        {
            sb.AppendLine($"  {(s.TotalSeconds ?? 0) / 60}min, {s.Sets.Count(x => x.Completed)}/{s.Sets.Count} sets" +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));
        }

        sb.AppendLine("MEALS TODAY:");
        var meals = await db.MealEntries.Where(m => m.Time >= dayStart && m.Time < dayEnd).OrderBy(m => m.Time).ToListAsync();
        if (meals.Count == 0) sb.AppendLine("(none yet)");
        foreach (var m in meals)
            sb.AppendLine($"  {m.Time:HH:mm} {m.MealType} \"{m.Description}\"" +
                          (string.IsNullOrWhiteSpace(m.PortionNote) ? "" : $" portion:\"{m.PortionNote}\"") +
                          (m.TagList.Count == 0 ? "" : $" tags:{string.Join("/", m.TagList)}") +
                          (m.Satiety == Satiety.Unset ? "" : $" satiety:{m.Satiety}"));

        sb.AppendLine("DRINKS TODAY:");
        var drinks = await db.DrinkEntries.Where(d => d.Time >= dayStart && d.Time < dayEnd).OrderBy(d => d.Time).ToListAsync();
        if (drinks.Count == 0) sb.AppendLine("(none yet)");
        foreach (var d in drinks)
            sb.AppendLine($"  {d.Time:HH:mm} \"{d.Description}\"" +
                          (d.Ounces is { } oz ? $" {oz:0.#}oz" : "") +
                          (d.SugarCount is { } su ? $" sugar:{su}" : ""));

        sb.AppendLine("PHYSICAL WORKLOAD TODAY:");
        var work = await db.PhysicalWorkloadEntries.Where(w => w.Date == today).ToListAsync();
        if (work.Count == 0) sb.AppendLine("(none)");
        foreach (var w in work)
            sb.AppendLine($"  {w.Activity} {(w.DurationMinutes is { } mins ? $"{mins}min " : "")}{w.Intensity}");

        // Compact 7-day rear-view so the model can tell one-offs from patterns.
        // Actual descriptions, not pre-judged counts — any claimed trend must be
        // grounded in entries it can name.
        sb.AppendLine("LAST 7 DAYS (pattern context — cite specific entries when claiming a trend; never extrapolate a pattern the data doesn't show):");
        var weekStart = dayStart.AddDays(-7);
        var weekMeals = await db.MealEntries.Where(m => m.Time >= weekStart && m.Time < dayStart).ToListAsync();
        var weekDrinks = await db.DrinkEntries.Where(d => d.Time >= weekStart && d.Time < dayStart).ToListAsync();
        for (var d = today.AddDays(-7); d < today; d = d.AddDays(1))
        {
            var ds = d.ToDateTime(TimeOnly.MinValue);
            var de = ds.AddDays(1);
            var dayMeals = weekMeals.Where(m => m.Time >= ds && m.Time < de).OrderBy(m => m.Time)
                .Select(m => Trunc(m.Description, 45)).ToList();
            var oz = weekDrinks.Where(x => x.Time >= ds && x.Time < de).Sum(x => x.Ounces ?? 0);
            sb.AppendLine($"  {d:MM-dd}: {(dayMeals.Count == 0 ? "(no meals logged)" : string.Join("; ", dayMeals))}" +
                          (oz > 0 ? $" | {oz:0}oz drinks" : ""));
        }

        static string Trunc(string s, int len) => s.Length <= len ? s : s[..(len - 1)] + "…";

        // Substance-cessation data is sensitive: included ONLY with explicit opt-in.
        if (Microsoft.Maui.Storage.Preferences.Default.Get(IncludeCessationPrefKey, false))
        {
            var goals = await db.CessationGoals.Where(g => g.Active).ToListAsync();
            if (goals.Count > 0)
            {
                sb.AppendLine("CESSATION GOALS (user opted in — be supportive; never judgmental about slips):");
                foreach (var g in goals)
                {
                    var todayEvents = await db.CessationEvents
                        .Where(e => e.GoalId == g.Id && e.Time >= dayStart && e.Time < dayEnd).ToListAsync();
                    var cravings = todayEvents.Count(e => e.Type == CessationEventType.Craving);
                    var usedToday = todayEvents.Where(e => e.Type == CessationEventType.Slip).Sum(e => e.Amount ?? 1);

                    if (g.Taper && g.TaperStartDate is { } start && today < g.QuitDate)
                    {
                        var total = g.QuitDate.DayNumber - start.DayNumber;
                        var allow = g.BaselineUnitsPerDay is { } b && total > 0
                            ? (int)Math.Ceiling(b * (g.QuitDate.DayNumber - today.DayNumber) / (double)total) : 0;
                        sb.AppendLine($"  {g.Substance}: tapering, quit day {g.QuitDate:yyyy-MM-dd}; " +
                                      $"today used {usedToday:0.#} of {allow} allowed, {cravings} craving(s)");
                        continue;
                    }

                    var daysQuit = today.DayNumber - g.QuitDate.DayNumber;
                    var lastSlip = await db.CessationEvents
                        .Where(e => e.GoalId == g.Id && e.Type == CessationEventType.Slip)
                        .OrderByDescending(e => e.Time).FirstOrDefaultAsync();
                    sb.AppendLine($"  {g.Substance}: quit {daysQuit} day(s) ago; today " +
                                  $"{cravings} craving(s), {usedToday:0.#} slip unit(s)" +
                                  (lastSlip is null ? "; no slips ever" : $"; last slip {DateOnly.FromDateTime(lastSlip.Time):yyyy-MM-dd}"));
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>Runs the daily check-in and parses the tone/synopsis/tips.</summary>
    public static async Task<DailyCheck> DailyCheckAsync(string apiKey, string prompt)
    {
        var text = await GenerateAsync(apiKey, prompt);
        if (string.IsNullOrWhiteSpace(text))
            return new("mixed", "Gemini returned an empty response.", new());
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var tone = root.TryGetProperty("tone", out var t) ? t.GetString()?.ToLowerInvariant() ?? "mixed" : "mixed";
            var synopsis = root.TryGetProperty("synopsis", out var s) ? s.GetString() ?? "" : text;
            var tips = new List<string>();
            if (root.TryGetProperty("tips", out var arr) && arr.ValueKind == JsonValueKind.Array)
                tips.AddRange(arr.EnumerateArray().Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!.Cast<string>());
            return new(tone, synopsis.Trim(), tips);
        }
        catch (JsonException)
        {
            return new("mixed", text.Trim(), new());
        }
    }

    public sealed record SuggestedExercise(string Name, bool IsNew, string? Muscles, string Measure,
        int Sets, int? Reps, int? DurationSeconds, int? RestSeconds);
    public sealed record RoutineSuggestion(string Name, string Rationale, List<SuggestedExercise> Exercises);

    /// <summary>Builds the routine-design prompt: exercise library, existing routines,
    /// and per-exercise 8-week stats, asking for a draft routine that covers
    /// under-trained muscle groups (inferred from exercise names).</summary>
    public static async Task<string> BuildRoutinePromptAsync(AppDbContext db, string? hint, bool bodyweightOnly = true)
    {
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sb = new StringBuilder();

        sb.AppendLine("You are a strength coach designing ONE new workout routine for one person from their training history.");
        sb.AppendLine("Goals: cover muscle groups the current training under-serves, keep continuity with exercises they already do, add a little novelty.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""
{
  "name": "<short routine name>",
  "rationale": "<2-4 sentences: which muscle groups the history under-trains and how this routine addresses them>",
  "exercises": [{
    "name": "<EXACT library name when reusing; clear conventional name when new>",
    "isNew": true | false,
    "muscles": "<primary muscles, e.g. chest/triceps>",
    "measure": "reps" | "duration",
    "sets": <int>, "reps": <int or null>, "durationSeconds": <int or null>, "restSeconds": <int>
  }]
}
""");
        sb.AppendLine("Rules: prefer library exercises (isNew=false, exact name and measure). " +
                      "Add NEW exercises (isNew=true) where the library lacks coverage for an under-trained muscle group.");
        sb.AppendLine("TIME BUDGET — the whole routine must finish in about 15-17 minutes (unless USER REQUEST says otherwise). " +
                      "Estimate sets × (work + rest), a reps set ≈ 40s of work. That usually means 4-6 exercises; fewer, harder exercises beat a long list.");
        sb.AppendLine("USE THE RATINGS — recent feedback, act on it:");
        sb.AppendLine("- rated Easy: FORBIDDEN — do not include this exercise at any targets. Replace it with a clearly harder variation under a DIFFERENT name (isNew=true), e.g. squats -> Bulgarian split squats, plank -> plank shoulder taps.");
        sb.AppendLine("- rated Moderate/Hard: include with targets slightly above the best shown (~5-10%).");
        sb.AppendLine("- rated VeryHard: keep targets at or slightly below the best shown.");
        sb.AppendLine("- PAIN flagged: exclude entirely.");
        sb.AppendLine(bodyweightOnly
            ? "EQUIPMENT — STRICT: every exercise must be doable with bodyweight alone (a mat/floor/wall is fine). No dumbbells, bands, bars, benches, or machines."
            : "EQUIPMENT: common home equipment is OK (dumbbells, bands, pull-up bar); prefer what the library's equipment notes already show.");
        if (!string.IsNullOrWhiteSpace(hint)) sb.AppendLine($"USER REQUEST (honor this): {hint.Trim()}");

        sb.AppendLine();
        sb.AppendLine("EXERCISE LIBRARY (name | measure | equipment):");
        var defs = await db.ExerciseDefinitions.Where(e => !e.Retired).OrderBy(e => e.Name).ToListAsync();
        if (defs.Count == 0) sb.AppendLine("(empty)");
        foreach (var d in defs)
            sb.AppendLine($"- {d.Name} | {(d.Measure == ExerciseMeasure.Duration ? "duration" : "reps")}" +
                          (string.IsNullOrWhiteSpace(d.EquipmentNotes) ? "" : $" | {d.EquipmentNotes}"));

        sb.AppendLine();
        sb.AppendLine("EXISTING ROUTINES (do not duplicate these):");
        var routines = await db.WorkoutRoutines
            .Include(r => r.Exercises).ThenInclude(e => e.ExerciseDefinition).ToListAsync();
        if (routines.Count == 0) sb.AppendLine("(none)");
        foreach (var r in routines)
            sb.AppendLine($"- {r.Name}: {string.Join(", ", r.Exercises.OrderBy(e => e.Order).Select(e => e.ExerciseDefinition?.Name ?? "?"))}");

        sb.AppendLine();
        sb.AppendLine("TRAINING HISTORY (last 8 weeks; per exercise: sessions, best set, latest rating):");
        var sessions = await db.WorkoutSessions.Where(s => s.Date >= since)
            .Include(s => s.Sets).ThenInclude(x => x.ExerciseDefinition)
            .Include(s => s.Feedback).ThenInclude(f => f.ExerciseDefinition)
            .OrderByDescending(s => s.Date).ToListAsync();
        var byExercise = sessions.SelectMany(s => s.Sets)
            .Where(x => x.ExerciseDefinition is not null)
            .GroupBy(x => x.ExerciseDefinition!.Name)
            .ToList();
        if (byExercise.Count == 0) sb.AppendLine("(none)");
        foreach (var g in byExercise)
        {
            var sessCount = sessions.Count(s => s.Sets.Any(x => x.ExerciseDefinition?.Name == g.Key));
            var bestSecs = g.Max(x => x.DurationSeconds ?? 0);
            var bestReps = g.Max(x => x.Reps ?? 0);
            var lastFb = sessions.SelectMany(s => s.Feedback)
                .FirstOrDefault(f => f.ExerciseDefinition?.Name == g.Key && f.Difficulty != Difficulty.Unset);
            sb.AppendLine($"- {g.Key}: {sessCount} session(s), best {(bestSecs > 0 ? $"{bestSecs}s" : $"{bestReps} reps")}" +
                          (lastFb is null ? "" : $", rated {lastFb.Difficulty}") +
                          (lastFb?.PainOrDiscomfort == true ? ", PAIN flagged" : ""));
        }

        return sb.ToString();
    }

    /// <summary>Exercises whose most recent difficulty rating in the window is Easy —
    /// used as a code-level backstop so the model can't sneak them back into drafts.</summary>
    public static async Task<HashSet<string>> RecentlyEasyExercisesAsync(AppDbContext db)
    {
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sessions = await db.WorkoutSessions.Where(s => s.Date >= since)
            .Include(s => s.Feedback).ThenInclude(f => f.ExerciseDefinition)
            .OrderByDescending(s => s.Date).ToListAsync();
        var latest = new Dictionary<string, Difficulty>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in sessions.SelectMany(s => s.Feedback))
            if (f.Difficulty != Difficulty.Unset && f.ExerciseDefinition?.Name is { } n && !latest.ContainsKey(n))
                latest[n] = f.Difficulty;
        return latest.Where(kv => kv.Value == Difficulty.Easy).Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Runs routine generation and parses the draft; null if unusable.</summary>
    public static async Task<RoutineSuggestion?> SuggestRoutineAsync(string apiKey, string prompt)
    {
        var text = await GenerateAsync(apiKey, prompt);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
            var rationale = root.TryGetProperty("rationale", out var ra) ? ra.GetString() ?? "" : "";
            var list = new List<SuggestedExercise>();
            if (root.TryGetProperty("exercises", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                {
                    var exName = e.TryGetProperty("name", out var en) ? en.GetString()?.Trim() : null;
                    if (string.IsNullOrWhiteSpace(exName)) continue;
                    list.Add(new(
                        exName,
                        e.TryGetProperty("isNew", out var inw) && inw.ValueKind == JsonValueKind.True,
                        e.TryGetProperty("muscles", out var mu) ? mu.GetString() : null,
                        e.TryGetProperty("measure", out var me) && me.GetString()?.ToLowerInvariant() == "duration" ? "duration" : "reps",
                        Math.Clamp(IntOrNull(e, "sets") ?? 3, 1, 10),
                        IntOrNull(e, "reps"),
                        IntOrNull(e, "durationSeconds"),
                        IntOrNull(e, "restSeconds")));
                }
            return list.Count == 0 ? null : new(string.IsNullOrEmpty(name) ? "AI Routine" : name, rationale.Trim(), list);
        }
        catch (JsonException)
        {
            return null;
        }

        static int? IntOrNull(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }

    public sealed record WorkloadSuggestion(string? Intensity, List<string> Areas, bool WorthLogging, string? Note);

    /// <summary>Advises on one physical-workload entry: intensity, affected body areas,
    /// and whether it's even worth logging (trivial chores are recovery noise).</summary>
    public static async Task<WorkloadSuggestion> SuggestWorkloadAsync(string apiKey, string activity,
        int? minutes, string? notes, IReadOnlyList<string> areaVocabulary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You advise on logging one non-workout physical activity in a fitness-recovery tracker.");
        sb.AppendLine("The log's purpose: capture activity that meaningfully taxes the body and affects recovery. Trivial chores are noise.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""{ "intensity": "Light" | "Moderate" | "Heavy", "areas": ["<from the list, only clearly affected>"], "worthLogging": true | false, "note": "<one short practical sentence: why, or how to log it better — e.g. 'Log the basket-carrying up stairs, skip the folding.'>" }""");
        sb.AppendLine($"Body areas (use these exact strings): {string.Join(" | ", areaVocabulary)}");
        sb.AppendLine();
        sb.AppendLine($"ACTIVITY: \"{activity}\"" + (minutes is { } m ? $" {m}min" : "") +
                      (string.IsNullOrWhiteSpace(notes) ? "" : $" notes:\"{notes}\""));

        var text = await GenerateAsync(apiKey, sb.ToString());
        if (string.IsNullOrWhiteSpace(text)) return new(null, new(), true, null);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var canon = areaVocabulary.ToDictionary(v => v, v => v, StringComparer.OrdinalIgnoreCase);
            var areas = new List<string>();
            if (root.TryGetProperty("areas", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                    if (a.GetString() is { } s && canon.TryGetValue(s.Trim(), out var c) && !areas.Contains(c))
                        areas.Add(c);
            return new(
                root.TryGetProperty("intensity", out var i) ? i.GetString() : null,
                areas,
                !root.TryGetProperty("worthLogging", out var w) || w.ValueKind != JsonValueKind.False,
                root.TryGetProperty("note", out var n) ? n.GetString() : null);
        }
        catch (JsonException)
        {
            return new(null, new(), true, null);
        }
    }

    public sealed record TagSuggestion(List<string> Known, string? Proposed);

    /// <summary>Suggests tags for one meal from its free-text description (meal text is
    /// already part of the analysis payloads, so this sends no new data category).
    /// Returns matches from <paramref name="vocabulary"/> plus at most one proposed new tag.</summary>
    public static async Task<TagSuggestion> SuggestMealTagsAsync(string apiKey, string mealType,
        string description, string? portionNote, IReadOnlyList<string> vocabulary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tag one logged meal for a personal nutrition tracker.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""{ "tags": ["<existing tags that clearly apply>"], "newTag": "<one new tag ONLY if something important has no existing tag, else null>" }""");
        sb.AppendLine($"Existing tags (use these exact strings, strongly prefer them): {string.Join(" | ", vocabulary)}");
        sb.AppendLine("Only include tags well supported by the text; when unsure, leave a tag out. " +
                      "A newTag must be short (1-3 words, e.g. 'High sugar'), broadly reusable, and not a synonym of an existing tag. " +
                      "Tags describe nutritional quality or food source. The entry already records its type " +
                      "(breakfast/lunch/dinner/snack/drink), time, and portion — NEVER suggest those as tags.");
        sb.AppendLine();
        sb.AppendLine($"MEAL: {mealType} \"{description}\"" +
                      (string.IsNullOrWhiteSpace(portionNote) ? "" : $" portion:\"{portionNote}\""));

        var text = await GenerateAsync(apiKey, sb.ToString());
        if (string.IsNullOrWhiteSpace(text)) return new(new(), null);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            // Map results back onto canonical vocabulary casing; drop anything else.
            var canon = vocabulary.ToDictionary(v => v, v => v, StringComparer.OrdinalIgnoreCase);
            var known = new List<string>();
            if (root.TryGetProperty("tags", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var t in arr.EnumerateArray())
                    if (t.GetString() is { } s && canon.TryGetValue(s.Trim(), out var c) && !known.Contains(c))
                        known.Add(c);
            var proposed = root.TryGetProperty("newTag", out var nt) ? nt.GetString()?.Trim() : null;
            if (!string.IsNullOrWhiteSpace(proposed))
            {
                // Near-duplicate of an existing tag (typo, plural, etc.) → select the
                // real tag instead of proposing a misspelled twin.
                var close = vocabulary.FirstOrDefault(v => Levenshtein(v, proposed) <= 2);
                if (close is not null)
                {
                    if (!known.Contains(close)) known.Add(close);
                    proposed = null;
                }
                else if (canon.ContainsKey(proposed) || IsRedundantTag(proposed))
                {
                    proposed = null;
                }
            }
            else proposed = null;
            return new(known, proposed);
        }
        catch (JsonException)
        {
            return new(new(), null);
        }
    }

    /// <summary>Case-insensitive edit distance, for catching typo'd near-duplicate tags.</summary>
    private static int Levenshtein(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>Proposed tags that duplicate structured fields the entry already has
    /// (meal type, timing) — e.g. "Snack" on a snack log — are rejected.</summary>
    private static bool IsRedundantTag(string tag) =>
        Enum.GetNames<MealType>().Any(t => tag.Contains(t, StringComparison.OrdinalIgnoreCase))
        || new[] { "meal", "drink", "morning", "evening", "late night" }
            .Any(w => tag.Equals(w, StringComparison.OrdinalIgnoreCase));

    public sealed record ExerciseAdvice(string Name, string Action, string? Target);
    public sealed record AiOutcome(string Analysis, List<ExerciseAdvice> Exercises, List<string> TopActions,
        List<string> MealFlags, string? BodyTrendStatus, string? BodyTrendNote);

    /// <summary>Raw JSON-mode generateContent call; returns the response text.</summary>
    private static async Task<string?> GenerateAsync(string apiKey, string prompt)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("x-goog-api-key", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { response_mime_type = "application/json" }
        }), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            // Surface Gemini's error message (bad key, quota, etc.) without the JSON noise.
            try
            {
                using var err = JsonDocument.Parse(body);
                throw new InvalidOperationException(
                    err.RootElement.GetProperty("error").GetProperty("message").GetString());
            }
            catch (KeyNotFoundException) { }
            catch (JsonException) { }
            throw new InvalidOperationException($"Gemini returned {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
    }

    /// <summary>Full 8-week analysis (JSON mode) returning the parsed outcome.</summary>
    public static async Task<AiOutcome> AnalyzeAsync(string apiKey, string prompt)
    {
        var text = await GenerateAsync(apiKey, prompt);
        if (string.IsNullOrWhiteSpace(text))
            return new("Gemini returned an empty response.", new(), new(), new(), null, null);

        // Parse the structured response; fall back to raw text if it isn't valid JSON.
        try
        {
            using var outDoc = JsonDocument.Parse(text);
            var root = outDoc.RootElement;
            var analysis = root.TryGetProperty("analysis", out var a) ? a.GetString() ?? "" : text;

            var exercises = new List<ExerciseAdvice>();
            if (root.TryGetProperty("exercises", out var exArr) && exArr.ValueKind == JsonValueKind.Array)
                foreach (var e in exArr.EnumerateArray())
                {
                    var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    exercises.Add(new(name,
                        e.TryGetProperty("action", out var act) ? act.GetString()?.ToLowerInvariant() ?? "hold" : "hold",
                        e.TryGetProperty("target", out var t) ? t.GetString() : null));
                }

            var actions = StringList(root, "topActions");
            var mealFlags = StringList(root, "mealFlags");

            string? bodyStatus = null, bodyNote = null;
            if (root.TryGetProperty("bodyTrend", out var bt) && bt.ValueKind == JsonValueKind.Object)
            {
                bodyStatus = bt.TryGetProperty("status", out var st) ? st.GetString()?.ToLowerInvariant() : null;
                bodyNote = bt.TryGetProperty("note", out var nt) ? nt.GetString() : null;
            }

            return new(analysis.Trim(), exercises, actions, mealFlags, bodyStatus, bodyNote);
        }
        catch (JsonException)
        {
            return new(text.Trim(), new(), new(), new(), null, null);
        }

        static List<string> StringList(JsonElement root, string prop)
        {
            var list = new List<string>();
            if (root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                list.AddRange(arr.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))!
                    .Cast<string>());
            return list;
        }
    }
}

/// <summary>Persists the latest structured AI advice so other screens
/// (routines, workout runner, dashboard) can show indicators.</summary>
public static class AiAdviceStore
{
    private static Microsoft.Maui.Storage.IPreferences Prefs => Microsoft.Maui.Storage.Preferences.Default;

    public static void Save(GeminiAnalyzer.AiOutcome outcome, string when)
    {
        Prefs.Set("ai_last_result", outcome.Analysis);
        Prefs.Set("ai_last_when", when);
        Prefs.Set("ai_exercises", JsonSerializer.Serialize(outcome.Exercises));
        Prefs.Set("ai_actions", JsonSerializer.Serialize(outcome.TopActions));
        Prefs.Set("ai_meal_flags", JsonSerializer.Serialize(outcome.MealFlags));
        Prefs.Set("ai_body_status", outcome.BodyTrendStatus ?? "");
        Prefs.Set("ai_body_note", outcome.BodyTrendNote ?? "");
    }

    public static List<string> LoadMealFlags()
    {
        try
        {
            var json = Prefs.Get<string?>("ai_meal_flags", null);
            return json is null ? new() : JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static (string? Status, string? Note) LoadBodyTrend()
    {
        var s = Prefs.Get("ai_body_status", "");
        var n = Prefs.Get("ai_body_note", "");
        return (s.Length == 0 ? null : s, n.Length == 0 ? null : n);
    }

    public static (string? Analysis, string? When) LoadAnalysis() =>
        (Prefs.Get<string?>("ai_last_result", null), Prefs.Get<string?>("ai_last_when", null));

    /// <summary>Per-exercise advice keyed by exercise name (case-insensitive).</summary>
    public static Dictionary<string, GeminiAnalyzer.ExerciseAdvice> LoadExercises()
    {
        try
        {
            var json = Prefs.Get<string?>("ai_exercises", null);
            if (json is null) return new(StringComparer.OrdinalIgnoreCase);
            var list = JsonSerializer.Deserialize<List<GeminiAnalyzer.ExerciseAdvice>>(json) ?? new();
            return list.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static List<string> LoadActions()
    {
        try
        {
            var json = Prefs.Get<string?>("ai_actions", null);
            return json is null ? new() : JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch { return new(); }
    }

    // ---- Daily check-in cache (one per calendar day) -----------------------------
    public static void SaveDaily(GeminiAnalyzer.DailyCheck check, string when)
    {
        Prefs.Set("ai_daily_date", DateTime.Now.ToString("yyyy-MM-dd"));
        Prefs.Set("ai_daily_when", when);
        Prefs.Set("ai_daily_tone", check.Tone);
        Prefs.Set("ai_daily_synopsis", check.Synopsis);
        Prefs.Set("ai_daily_tips", JsonSerializer.Serialize(check.Tips));
    }

    /// <summary>Today's cached check-in, or null if none was run today.</summary>
    public static (GeminiAnalyzer.DailyCheck Check, string When)? LoadDaily()
    {
        if (Prefs.Get("ai_daily_date", "") != DateTime.Now.ToString("yyyy-MM-dd")) return null;
        try
        {
            var tips = JsonSerializer.Deserialize<List<string>>(Prefs.Get("ai_daily_tips", "[]")) ?? new();
            return (new(Prefs.Get("ai_daily_tone", "mixed"), Prefs.Get("ai_daily_synopsis", ""), tips),
                    Prefs.Get("ai_daily_when", ""));
        }
        catch { return null; }
    }

    public static string Glyph(string action) => action switch
    {
        "progress" => "⬆", "backoff" => "⬇", _ => "⏸"
    };

    public static string BadgeClass(string action) => action switch
    {
        "progress" => "good", "backoff" => "warn", _ => ""
    };
}

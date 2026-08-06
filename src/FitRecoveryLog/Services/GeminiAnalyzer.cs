using System.Text;
using System.Text.Json;
using FitRecoveryLog.Application.Ai;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>Preferences key for the daily protein target in grams (0 = unset).</summary>
    public const string ProteinGoalPrefKey = "protein_goal_g";

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

    /// <summary>The user's numeric daily macro/hydration targets (ranges), so the AI
    /// can judge intake against them. Ranges are targets, not hard rules — being a bit
    /// under or over on a given day is normal; coach the pattern, not one number.</summary>
    private static void AppendMacroGoalTargets(StringBuilder sb)
    {
        var p = NutritionGoals.Get(NutritionGoals.Protein);
        var f = NutritionGoals.Get(NutritionGoals.Fat);
        var fib = NutritionGoals.Get(NutritionGoals.Fiber);
        sb.AppendLine("DAILY MACRO/HYDRATION TARGETS (ranges are goals, not hard limits — a day slightly " +
                      "under/over is fine; flag only consistent misses). Calories, carbs, and water have " +
                      "separate rest-day and workout-day ranges — use the one matching today's day type:");
        if (p.IsSet) sb.AppendLine($"  - Protein: {p.Min}-{p.Max} g");
        if (f.IsSet) sb.AppendLine($"  - Fat: {f.Min}-{f.Max} g");
        if (fib.IsSet) sb.AppendLine($"  - Fiber: {fib.Min}-{fib.Max} g");
        sb.AppendLine($"  - Added sugar: stay under {NutritionGoals.AddedSugarMax} g (less is better)");
        Split("Calories", NutritionGoals.Calories, "");
        Split("Carbohydrates", NutritionGoals.Carbs, " g");
        Split("Fluids (all drinks)", NutritionGoals.Water, " oz");

        void Split(string label, string key, string unit)
        {
            var rest = NutritionGoals.GetDay(key, false);
            var act = NutritionGoals.GetDay(key, true);
            if (rest.IsSet || act.IsSet)
                sb.AppendLine($"  - {label}: rest-day {rest.Min}-{rest.Max}{unit}, workout-day {act.Min}-{act.Max}{unit}");
        }
    }
    private const int WindowDays = 56; // 8 weeks

    // Transitional service-locator: pages still call the static GeminiAnalyzer API while they're
    // migrated onto the injected ports (ILlmClient / ILlmKeyStore) in later commits.
    private static T Resolve<T>() where T : notnull =>
        IPlatformApplication.Current!.Services.GetRequiredService<T>();

    /// <summary>Stores the API key. Now delegates to the <see cref="ILlmKeyStore"/> port
    /// (Keychain-backed); kept as a static shim until callers move to the injected port.</summary>
    public static class KeyStore
    {
        public static Task<string?> GetAsync() => Resolve<ILlmKeyStore>().GetAsync();
        public static Task SetAsync(string value) => Resolve<ILlmKeyStore>().SetAsync(value);
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
  "mealFlags": ["<0-2 short, balanced notes — a positive is fine ('Sweet tea down to ~16oz/day — on target'); reserve concerns for genuine multi-week patterns, never single items or 'cut it out' advice>"],
  "bodyTrend": { "status": "on-track" | "off-track" | "unclear", "note": "<one short sentence, e.g. 'Weight down ~1 lb/week'>" }
}
""");
        sb.AppendLine("Be specific and reference the data. Say plainly where data is too sparse to conclude anything.");
        sb.AppendLine("DIET FRAMING — assess intake AS A WHOLE, balanced, not a hunt for negatives:");
        sb.AppendLine("- Judge the overall diet and its TREND across the 8 weeks, not isolated items. Lead with what's working; raise at most 1-2 things genuinely worth attention.");
        sb.AppendLine("- Coach MODERATION, never ELIMINATION. Do NOT recommend cutting sugar, sodium, sweet drinks, or any food to zero. Reasonable amounts relative to overall intake are fine.");
        sb.AppendLine("- Judge QUANTITY and TREND, not frequency. Use the weekly volume data, but do NOT call rising logged volume an 'increasing consumption trend': early weeks usually have sparse/partial logging, so a rise across weeks typically means MORE COMPLETE LOGGING, not more intake. Only call a trend real if logging is consistent throughout — otherwise say the trend is unclear and defer to the user's stated goals for the real baseline/direction.");
        sb.AppendLine("- Meal TAGS (e.g. 'High sodium', 'High sugar') are AI-suggested heuristics that over-apply; do NOT treat tag frequency as proof of a dietary pattern. Judge the actual foods eaten, not how often a tag appears.");
        sb.AppendLine("- Judge the FOOD, not the venue: a grilled chicken sandwich from a drive-thru is a reasonable protein choice; don't penalize restaurant/fast-food as a category.");
        sb.AppendLine("- Zero-sugar drinks (Coke Zero, diet, sugar-free) are NOT sugary drinks — taste variety, not a concern.");
        sb.AppendLine("- Keep sugar in PROPORTION: a small treat (~15g or less; a banana is ~14g) is normal. Flag sugar only when daily totals are genuinely high or a frequent pattern.");
        sb.AppendLine("- Keep sodium in PROPORTION too: normal seasoned or home-cooked meals and an occasional restaurant meal are fine — never suggest zero/low sodium; flag only a consistently high-sodium pattern.");
        sb.AppendLine("- HIGH BAR for any sugar/sodium concern: never call ordinary eating — a treat, cereal, or a restaurant meal — a concerning 'pattern'. Raise sugar or sodium ONLY if you can cite a specific, genuinely excessive quantity from the data. If you can't cite a real number, don't raise it. Do not bundle unrelated items into a vague pattern.");
        sb.AppendLine("- Against a stated goal, small overages (within ~25%, e.g. 20oz vs a 16oz goal) are ON-TRACK — mention neutrally at most; reserve 'significantly above' for large, sustained excess.");
        AppendUserGoals(sb);
        AppendMacroGoalTargets(sb);
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
                          (s.SleepScore is { } sc ? $" score:{sc}{(s.ScoreEstimated ? "(rough estimate, not Apple's)" : "")}" : "") +
                          (s.Interruptions is { } i ? $" interruptions:{i}" : "") +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));

        return sb.ToString();
    }

    /// <summary>Muscle groups the app tags exercises with — must match the Exercise
    /// Library picker so the model uses consistent names.</summary>
    private static readonly string[] MuscleCatalog =
        { "Chest", "Back", "Shoulders", "Biceps", "Triceps", "Forearms", "Core", "Glutes", "Quads", "Hamstrings", "Calves" };

    /// <summary>Builds the routine-design prompt: exercise library (with muscle tags),
    /// existing routines, per-exercise 8-week stats, and real per-muscle set volume,
    /// asking for a draft routine that prioritizes under-trained muscle groups.</summary>
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
        sb.AppendLine("TARGET BALANCE — use the MUSCLE VOLUME data below (real sets per muscle group): prioritize the " +
                      $"lowest-volume and untrained groups. Set each exercise's \"muscles\" from this list: {string.Join(", ", MuscleCatalog)}.");
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
        sb.AppendLine("EXERCISE LIBRARY (name | measure | muscles | equipment):");
        var defs = await db.ExerciseDefinitions.Where(e => !e.Retired).OrderBy(e => e.Name).ToListAsync();
        if (defs.Count == 0) sb.AppendLine("(empty)");
        foreach (var d in defs)
            sb.AppendLine($"- {d.Name} | {(d.Measure == ExerciseMeasure.Duration ? "duration" : "reps")}" +
                          $" | {(string.IsNullOrWhiteSpace(d.MuscleGroups) ? "muscles unset" : d.MuscleGroups)}" +
                          (string.IsNullOrWhiteSpace(d.EquipmentNotes) ? "" : $" | {d.EquipmentNotes}"));

        sb.AppendLine();
        sb.AppendLine("EXISTING ROUTINES (do not duplicate these):");
        var routines = await db.WorkoutRoutines
            .Include(r => r.Exercises).ThenInclude(e => e.ExerciseDefinition)
            .Where(r => !r.Archived).ToListAsync();
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

        // Per-muscle set volume over the window, from each exercise's muscle-group tags.
        // This is the real balance signal — target the least-worked groups.
        var muscleByName = defs.ToDictionary(d => d.Name, d => d.MuscleGroupList, StringComparer.OrdinalIgnoreCase);
        var volume = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sessions.SelectMany(s => s.Sets))
        {
            var name = set.ExerciseDefinition?.Name;
            if (name is null || !muscleByName.TryGetValue(name, out var groups)) continue;
            foreach (var mg in groups) volume[mg] = volume.GetValueOrDefault(mg) + 1;
        }
        sb.AppendLine();
        if (volume.Count > 0)
        {
            sb.AppendLine("MUSCLE VOLUME (last 8 weeks, total sets per group — LOW groups are under-trained; prioritize them):");
            foreach (var kv in volume.OrderBy(x => x.Value))
                sb.AppendLine($"- {kv.Key}: {kv.Value} sets");
            var untouched = MuscleCatalog.Where(m => !volume.ContainsKey(m)).ToList();
            if (untouched.Count > 0)
                sb.AppendLine($"- Not trained at all: {string.Join(", ", untouched)}");
        }
        else
        {
            sb.AppendLine("MUSCLE VOLUME: unavailable — library exercises have no muscle-group tags yet. Infer muscle groups from exercise names instead.");
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

    /// <summary>Provider-agnostic JSON-mode generate call. Delegates to the registered
    /// <see cref="ILlmClient"/> adapter (Gemini today). The <paramref name="apiKey"/> parameter
    /// is legacy — the adapter now owns the key — and is ignored; callers drop it as they migrate.</summary>
    private static Task<string?> GenerateAsync(string apiKey, string prompt, byte[]? imageJpeg = null) =>
        Resolve<ILlmClient>().GenerateJsonAsync(prompt, imageJpeg);

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

    public static void Save(AiOutcome outcome, string when)
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
    public static Dictionary<string, ExerciseAdvice> LoadExercises()
    {
        try
        {
            var json = Prefs.Get<string?>("ai_exercises", null);
            if (json is null) return new(StringComparer.OrdinalIgnoreCase);
            var list = JsonSerializer.Deserialize<List<ExerciseAdvice>>(json) ?? new();
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
    public static void SaveDaily(DailyCheck check, string when)
    {
        Prefs.Set("ai_daily_date", DateTime.Now.ToString("yyyy-MM-dd"));
        Prefs.Set("ai_daily_when", when);
        Prefs.Set("ai_daily_tone", check.Tone);
        Prefs.Set("ai_daily_synopsis", check.Synopsis);
        Prefs.Set("ai_daily_tips", JsonSerializer.Serialize(check.Tips));
    }

    /// <summary>Today's cached check-in, or null if none was run today.</summary>
    public static (DailyCheck Check, string When)? LoadDaily()
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

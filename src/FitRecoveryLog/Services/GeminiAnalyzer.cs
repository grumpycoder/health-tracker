using System.Text;
using System.Text.Json;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Services;

/// <summary>
/// AI analysis of workouts, meals/drinks, sleep, and body-measurement numbers
/// via the Gemini API (free AI Studio tier; the user supplies their own key in
/// Settings). Medications, labs, notes, photos, and freeform measurement text
/// are deliberately NEVER sent.
/// </summary>
public static class GeminiAnalyzer
{
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
  "mealFlags": ["<0-3 short flags about eating/drinking habits worth attention, e.g. 'Watch sweet tea: 3x/week'>"],
  "bodyTrend": { "status": "on-track" | "off-track" | "unclear", "note": "<one short sentence, e.g. 'Weight down ~1 lb/week'>" }
}
""");
        sb.AppendLine("Be specific and reference the data. Say plainly where data is too sparse to conclude anything.");
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
        sb.AppendLine("Consider meal quality/timing/junk food, sugary drinks, sleep duration and score, whether a workout happened on a workout day, and physical workload. If little is logged yet, say so and suggest what to log.");
        sb.AppendLine();
        sb.AppendLine($"NOW: {now:yyyy-MM-dd HH:mm} ({now.DayOfWeek})");

        var day = await db.DailyLogs.FirstOrDefaultAsync(x => x.Date == today);
        sb.AppendLine($"PLANNED DAY TYPE: {(day is null || day.DayType == DayType.Unset ? "(not set)" : day.DayType.ToString())}");

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

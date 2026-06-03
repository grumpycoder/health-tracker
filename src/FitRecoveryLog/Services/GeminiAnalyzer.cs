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

    public sealed record ExerciseAdvice(string Name, string Action, string? Target);
    public sealed record AiOutcome(string Analysis, List<ExerciseAdvice> Exercises, List<string> TopActions,
        List<string> MealFlags, string? BodyTrendStatus, string? BodyTrendNote);

    /// <summary>Calls Gemini generateContent (JSON mode) and returns the parsed outcome.</summary>
    public static async Task<AiOutcome> AnalyzeAsync(string apiKey, string prompt)
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
        var text = doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
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

    public static string Glyph(string action) => action switch
    {
        "progress" => "⬆", "backoff" => "⬇", _ => "⏸"
    };

    public static string BadgeClass(string action) => action switch
    {
        "progress" => "good", "backoff" => "warn", _ => ""
    };
}

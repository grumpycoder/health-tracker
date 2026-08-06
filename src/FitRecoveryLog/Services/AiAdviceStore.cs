using System.Text.Json;
using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Services;

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

using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

public enum GoalStatus { Unset, Under, InRange, Over }

/// <summary>A min–max daily target for a macro (oz for water).</summary>
public sealed record MacroGoal(int Min, int Max)
{
    public bool IsSet => Max > 0;
}

/// <summary>Preference-backed daily nutrition goals (ranges), seeded with the user's
/// stated targets and editable in Settings. Calories, water, and carbs vary by day
/// type (active vs rest); protein/fat/fiber are single ranges; added sugar is a ceiling.</summary>
public static class NutritionGoals
{
    // Single-range goals.
    public const string Protein = "goal_protein";
    public const string Fat = "goal_fat";
    public const string Fiber = "goal_fiber";
    public const string AddedSugarMaxKey = "goal_addedsugar_max";
    // Day-type-split goals (active vs rest).
    public const string Calories = "goal_calories";
    public const string Carbs = "goal_carbs";
    public const string Water = "goal_water";                 // ounces

    private static readonly Dictionary<string, MacroGoal> Defaults = new()
    {
        [Protein] = new(150, 170),
        [Fat] = new(55, 70),
        [Fiber] = new(30, 40),
    };

    // Split defaults: (rest, active). Seeded so active ≥ rest; all editable.
    private static readonly Dictionary<string, (MacroGoal RestGoal, MacroGoal ActiveGoal)> SplitDefaults = new()
    {
        [Calories] = (new(0, 0), new(0, 0)),          // unset until the user sets them
        [Carbs] = (new(175, 225), new(175, 225)),
        [Water] = (new(100, 120), new(100, 120)),     // user raises the active side
    };

    public static MacroGoal Get(string key)
    {
        var d = Defaults.TryGetValue(key, out var def) ? def : new MacroGoal(0, 0);
        return new MacroGoal(
            Preferences.Default.Get($"{key}_min", d.Min),
            Preferences.Default.Get($"{key}_max", d.Max));
    }

    public static void Set(string key, int min, int max)
    {
        Preferences.Default.Set($"{key}_min", Math.Max(0, min));
        Preferences.Default.Set($"{key}_max", Math.Max(0, max));
    }

    /// <summary>Day-type-specific range (active vs rest) for calories/carbs/water.</summary>
    public static MacroGoal GetDay(string key, bool active)
    {
        var suffix = active ? "active" : "rest";
        var d = SplitDefaults.TryGetValue(key, out var def)
            ? (active ? def.ActiveGoal : def.RestGoal) : new MacroGoal(0, 0);
        return new MacroGoal(
            Preferences.Default.Get($"{key}_{suffix}_min", d.Min),
            Preferences.Default.Get($"{key}_{suffix}_max", d.Max));
    }

    public static void SetDay(string key, bool active, int min, int max)
    {
        var suffix = active ? "active" : "rest";
        Preferences.Default.Set($"{key}_{suffix}_min", Math.Max(0, min));
        Preferences.Default.Set($"{key}_{suffix}_max", Math.Max(0, max));
    }

    public static int AddedSugarMax => Preferences.Default.Get(AddedSugarMaxKey, 30);
    public static void SetAddedSugarMax(int max) => Preferences.Default.Set(AddedSugarMaxKey, Math.Max(0, max));

    public static GoalStatus Status(double value, MacroGoal g) =>
        !g.IsSet ? GoalStatus.Unset
        : value < g.Min ? GoalStatus.Under
        : value > g.Max ? GoalStatus.Over
        : GoalStatus.InRange;

    /// <summary>Ceiling status (added sugar): fine at/under the cap, over above it.</summary>
    public static GoalStatus CeilingStatus(double value, int max) =>
        max <= 0 ? GoalStatus.Unset : value > max ? GoalStatus.Over : GoalStatus.InRange;

    public static string Color(GoalStatus s) => s switch
    {
        GoalStatus.InRange => "#3fa34d",   // green
        GoalStatus.Over => "#c47f00",      // amber
        GoalStatus.Under => "#4a72d0",     // blue (below range)
        _ => "var(--muted)"
    };
}

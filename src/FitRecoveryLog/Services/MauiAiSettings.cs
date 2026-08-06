using FitRecoveryLog.Application.Ai;
using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

/// <summary>Preferences-backed <see cref="IAiSettings"/> for the phone. Uses the same keys the
/// Settings screen writes, and snapshots the macro targets from <see cref="NutritionGoals"/>.</summary>
public sealed class MauiAiSettings : IAiSettings
{
    /// <summary>Preferences keys — shared so the Settings/Cessation screens write the same slots
    /// this adapter reads.</summary>
    public const string CoachingGoalsKey = "ai_user_goals";
    public const string IncludeCessationKey = "ai_include_cessation";

    public string? CoachingGoals => Preferences.Default.Get<string?>(CoachingGoalsKey, null);

    public bool IncludeCessationData => Preferences.Default.Get(IncludeCessationKey, false);

    public MacroTargets MacroTargets => new(
        Range(NutritionGoals.Protein), Range(NutritionGoals.Fat), Range(NutritionGoals.Fiber),
        NutritionGoals.AddedSugarMax,
        DayRange(NutritionGoals.Calories, false), DayRange(NutritionGoals.Calories, true),
        DayRange(NutritionGoals.Carbs, false), DayRange(NutritionGoals.Carbs, true),
        DayRange(NutritionGoals.Water, false), DayRange(NutritionGoals.Water, true));

    private static MacroRange Range(string key)
    {
        var g = NutritionGoals.Get(key);
        return new(g.Min, g.Max);
    }

    private static MacroRange DayRange(string key, bool active)
    {
        var g = NutritionGoals.GetDay(key, active);
        return new(g.Min, g.Max);
    }
}

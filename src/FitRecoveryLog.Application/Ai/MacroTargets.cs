namespace FitRecoveryLog.Application.Ai;

/// <summary>A min–max daily target (0/0 = unset).</summary>
public sealed record MacroRange(int Min, int Max)
{
    public bool IsSet => Max > 0;
    public static readonly MacroRange Unset = new(0, 0);
}

/// <summary>
/// The user's numeric daily macro/hydration targets, snapshotted for prompt building so the
/// coach can render them without reaching into platform settings. Calories/carbs/water split by
/// day type (rest vs active); protein/fat/fiber are single ranges; added sugar is a ceiling.
/// </summary>
public sealed record MacroTargets(
    MacroRange Protein, MacroRange Fat, MacroRange Fiber, int AddedSugarMax,
    MacroRange CaloriesRest, MacroRange CaloriesActive,
    MacroRange CarbsRest, MacroRange CarbsActive,
    MacroRange WaterRest, MacroRange WaterActive)
{
    public static readonly MacroTargets None = new(
        MacroRange.Unset, MacroRange.Unset, MacroRange.Unset, 0,
        MacroRange.Unset, MacroRange.Unset, MacroRange.Unset, MacroRange.Unset, MacroRange.Unset, MacroRange.Unset);
}

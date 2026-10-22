namespace FitRecoveryLog.Data;

/// <summary>Calories out for the energy-balance line, shared by the phone and web so both agree.
/// Prefers Apple Health (resting + active) for a completed day; otherwise a rough estimate:
/// BMR (Hume, else weight×10) × 1.25 light activity + workout burn at MET ~5.</summary>
public static class EnergyMath
{
    public readonly record struct EnergyOut(int Kcal, bool FromHealth);

    public static EnergyOut? Out(DailyLog? day, DateOnly today, int? bmr, double? weightLbs, int workoutSeconds)
    {
        // Health's active energy already contains the workout (the Watch's measurement, or the
        // estimate this app writes per session), so workout burn is NOT added on top of it.
        if (day is { RestingEnergyKcal: > 0 and var rest, ActiveEnergyKcal: { } active } && day.Date < today)
            return new(rest + active, true);

        var baseBmr = bmr ?? (weightLbs is { } w and > 0 ? (int)(w * 10) : (int?)null);
        if (baseBmr is null) return null;
        var lbs = weightLbs ?? 0;
        var workout = lbs <= 0 || workoutSeconds <= 0 ? 0
            : (int)Math.Round(workoutSeconds / 60.0 * 5 * 3.5 * (lbs * 0.453592) / 200.0);
        return new((int)Math.Round(baseBmr.Value * 1.25) + workout, false);
    }
}

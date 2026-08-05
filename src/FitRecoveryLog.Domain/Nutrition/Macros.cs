using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Nutrition;

/// <summary>
/// The nutrition content of something eaten or drunk. A value object shared by meals and
/// drinks. Each component is nullable to distinguish <em>unknown</em> (never logged) from
/// <em>zero</em> — a meal with no macros must not read as 0 calories in a daily total.
/// Immutable; operations return new instances.
/// </summary>
public sealed class Macros : ValueObject
{
    public int? Calories { get; }
    public double? ProteinG { get; }
    public double? CarbsG { get; }
    public double? FatG { get; }
    public double? FiberG { get; }
    public double? SugarG { get; }
    public double? AddedSugarG { get; }
    public int? SodiumMg { get; }

    /// <summary>All components unknown.</summary>
    public static readonly Macros None = new(null, null, null, null, null, null, null, null);

    public Macros(int? calories, double? proteinG, double? carbsG, double? fatG,
                  double? fiberG, double? sugarG, double? addedSugarG, int? sodiumMg)
    {
        // null components are "unknown" and allowed; present components must be non-negative.
        if (calories < 0 || proteinG < 0 || carbsG < 0 || fatG < 0 ||
            fiberG < 0 || sugarG < 0 || addedSugarG < 0 || sodiumMg < 0)
            throw new ArgumentOutOfRangeException(nameof(calories), "Macro components cannot be negative.");

        Calories = calories;
        ProteinG = proteinG;
        CarbsG = carbsG;
        FatG = fatG;
        FiberG = fiberG;
        SugarG = sugarG;
        AddedSugarG = addedSugarG;
        SodiumMg = sodiumMg;
    }

    /// <summary>True if any component has been logged.</summary>
    public bool HasAny =>
        Calories is not null || ProteinG is not null || CarbsG is not null || FatG is not null ||
        FiberG is not null || SugarG is not null || AddedSugarG is not null || SodiumMg is not null;

    /// <summary>Scale every known component (e.g. label per-serving × servings eaten).
    /// Unknown components stay unknown.</summary>
    public Macros Scale(double factor)
    {
        if (factor < 0) throw new ArgumentOutOfRangeException(nameof(factor), "Factor cannot be negative.");
        return new Macros(
            Calories is int c ? (int)Math.Round(c * factor, MidpointRounding.AwayFromZero) : null,
            ProteinG * factor, CarbsG * factor, FatG * factor,
            FiberG * factor, SugarG * factor, AddedSugarG * factor,
            SodiumMg is int s ? (int)Math.Round(s * factor, MidpointRounding.AwayFromZero) : null);
    }

    /// <summary>Component-wise sum (for daily totals). Unknown + unknown stays unknown;
    /// unknown + value contributes the value.</summary>
    public Macros Add(Macros other) => new(
        Sum(Calories, other.Calories),
        Sum(ProteinG, other.ProteinG),
        Sum(CarbsG, other.CarbsG),
        Sum(FatG, other.FatG),
        Sum(FiberG, other.FiberG),
        Sum(SugarG, other.SugarG),
        Sum(AddedSugarG, other.AddedSugarG),
        Sum(SodiumMg, other.SodiumMg));

    public static Macros operator +(Macros left, Macros right) => left.Add(right);

    private static int? Sum(int? a, int? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
    private static double? Sum(double? a, double? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Calories;
        yield return ProteinG;
        yield return CarbsG;
        yield return FatG;
        yield return FiberG;
        yield return SugarG;
        yield return AddedSugarG;
        yield return SodiumMg;
    }
}

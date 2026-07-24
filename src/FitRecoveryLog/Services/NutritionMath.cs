using FitRecoveryLog.Data;

namespace FitRecoveryLog.Services;

/// <summary>Daily nutrition roll-ups shared by the Home dashboard and the Meals Log
/// tab, so both compute totals the same way. Totals only cover items that carry
/// macros; sugar uses a scanned drink's grams when present, else the teaspoon count
/// (~4g each) so added-sugar coffee still counts without double-counting.</summary>
public static class NutritionMath
{
    public static int Calories(IEnumerable<MealEntry> meals, IEnumerable<DrinkEntry> drinks) =>
        meals.Sum(m => m.Calories ?? 0) + drinks.Sum(d => d.Calories ?? 0);

    public static double Protein(IEnumerable<MealEntry> meals, IEnumerable<DrinkEntry> drinks) =>
        meals.Sum(m => m.ProteinG ?? 0) + drinks.Sum(d => d.ProteinG ?? 0);

    public static double Sodium(IEnumerable<MealEntry> meals, IEnumerable<DrinkEntry> drinks) =>
        meals.Sum(m => m.SodiumMg ?? 0) + drinks.Sum(d => d.SodiumMg ?? 0);

    public static double Sugar(IEnumerable<MealEntry> meals, IEnumerable<DrinkEntry> drinks) =>
        meals.Sum(m => m.SugarG ?? 0)
        + drinks.Sum(d => d.SugarG ?? (d.SugarCount ?? 0) * 4.0);

    public static bool HasData(IEnumerable<MealEntry> meals, IEnumerable<DrinkEntry> drinks) =>
        meals.Any(m => m.Calories is not null || m.ProteinG is not null)
        || drinks.Any(d => d.Calories is not null || d.ProteinG is not null
                        || d.SugarG is not null || d.SugarCount is > 0);
}

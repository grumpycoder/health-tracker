namespace FitRecoveryLog.Domain.Nutrition;

/// <summary>Kind of eating occasion. Domain-owned; mapped to storage by ordinal.</summary>
public enum MealType
{
    Breakfast = 0,
    Lunch,
    Dinner,
    Snack,
    Drink,
}

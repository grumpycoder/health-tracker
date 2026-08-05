namespace FitRecoveryLog.Domain.Nutrition;

/// <summary>How a meal left you feeling. Domain-owned; mapped to storage by ordinal.</summary>
public enum Satiety
{
    Unset = 0,
    StillHungry,
    Satisfied,
    Full,
    Bloated,
    EmptyStomach,
}

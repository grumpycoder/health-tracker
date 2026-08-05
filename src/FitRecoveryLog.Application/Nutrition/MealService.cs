using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;

namespace FitRecoveryLog.Application.Nutrition;

public interface IMealRepository
{
    Task<Meal?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Meal>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(Meal meal, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

/// <summary>All of a meal's editable fields — a meal is saved as a whole (coarse use case),
/// matching how the app logs one.</summary>
public sealed record MealData(
    DateTime Time, MealType MealType, string? Description, string? PortionNote,
    Satiety Satiety, int? QualityStars, Macros Macros, Tags Tags);

public sealed class MealService
{
    private readonly IMealRepository _meals;
    public MealService(IMealRepository meals) => _meals = meals;

    public Task<IReadOnlyList<Meal>> ListAsync(CancellationToken ct = default) => _meals.ListAsync(ct);
    public Task<Meal?> GetAsync(Guid id, CancellationToken ct = default) => _meals.GetAsync(id, ct);

    public async Task<Result<Guid>> CreateAsync(MealData data, CancellationToken ct = default)
    {
        var meal = Meal.Create(data.Time, data.MealType, data.Description);
        Apply(meal, data);
        await _meals.SaveAsync(meal, ct);
        return Result<Guid>.Success(meal.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, MealData data, CancellationToken ct = default)
    {
        var meal = await _meals.GetAsync(id, ct);
        if (meal is null) return Result.Failure("Meal not found.");
        Apply(meal, data);
        await _meals.SaveAsync(meal, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var meal = await _meals.GetAsync(id, ct);
        if (meal is null) return Result.Failure("Meal not found.");
        await _meals.RemoveAsync(id, ct);
        return Result.Success();
    }

    private static void Apply(Meal meal, MealData d)
    {
        meal.SetTime(d.Time);
        meal.SetMealType(d.MealType);
        meal.SetDescription(d.Description);
        meal.SetPortionNote(d.PortionNote);
        meal.SetSatiety(d.Satiety);
        meal.SetQualityStars(d.QualityStars);
        meal.SetMacros(d.Macros);
        meal.SetTags(d.Tags);
    }
}

using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;

namespace FitRecoveryLog.Application.Nutrition;

public interface IDrinkRepository
{
    Task<Drink?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Drink>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(Drink drink, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

public sealed record DrinkData(DateTime Time, string? Description, double? Ounces, int? SugarCount, Macros Macros, Tags Tags);

public sealed class DrinkService
{
    private readonly IDrinkRepository _drinks;
    public DrinkService(IDrinkRepository drinks) => _drinks = drinks;

    public Task<IReadOnlyList<Drink>> ListAsync(CancellationToken ct = default) => _drinks.ListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(DrinkData data, CancellationToken ct = default)
    {
        var drink = Drink.Create(data.Time, data.Description);
        try { Apply(drink, data); }
        catch (ArgumentOutOfRangeException ex) { return Result<Guid>.Failure(ex.Message); }
        await _drinks.SaveAsync(drink, ct);
        return Result<Guid>.Success(drink.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, DrinkData data, CancellationToken ct = default)
    {
        var drink = await _drinks.GetAsync(id, ct);
        if (drink is null) return Result.Failure("Drink not found.");
        try { Apply(drink, data); }
        catch (ArgumentOutOfRangeException ex) { return Result.Failure(ex.Message); }
        await _drinks.SaveAsync(drink, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var drink = await _drinks.GetAsync(id, ct);
        if (drink is null) return Result.Failure("Drink not found.");
        await _drinks.RemoveAsync(id, ct);
        return Result.Success();
    }

    private static void Apply(Drink drink, DrinkData d)
    {
        drink.SetTime(d.Time);
        drink.SetDescription(d.Description);
        drink.SetOunces(d.Ounces);
        drink.SetSugarCount(d.SugarCount);
        drink.SetMacros(d.Macros);
        drink.SetTags(d.Tags);
    }
}

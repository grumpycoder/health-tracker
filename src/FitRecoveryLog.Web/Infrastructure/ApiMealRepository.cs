using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Web implementation of <see cref="IMealRepository"/> over the sync API. Maps the
/// <see cref="Meal"/> aggregate (with its Macros/Tags value objects and domain enums) to/from
/// the persistence entity.</summary>
public sealed class ApiMealRepository : IMealRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiMealRepository(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    public async Task<Meal?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.MealEntry>(pull).FirstOrDefault(m => m.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Meal>> ListAsync(CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        return WebSyncClient.Rows<Persistence.MealEntry>(pull).Select(ToDomain).ToList();
    }

    public async Task SaveAsync(Meal meal, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.MealEntry>(pull).FirstOrDefault(m => m.Id == meal.Id);
        await _sync.PushAsync(new Persistence.MealEntry
        {
            Id = meal.Id,
            Time = meal.Time,
            MealType = (Persistence.MealType)(int)meal.MealType,
            Description = meal.Description,
            PortionNote = meal.PortionNote,
            Satiety = (Persistence.Satiety)(int)meal.Satiety,
            QualityStars = meal.QualityStars,
            Tags = meal.Tags.ToCsv(),
            Calories = meal.Macros.Calories,
            ProteinG = meal.Macros.ProteinG,
            CarbsG = meal.Macros.CarbsG,
            SugarG = meal.Macros.SugarG,
            FatG = meal.Macros.FatG,
            SodiumMg = meal.Macros.SodiumMg,
            FiberG = meal.Macros.FiberG,
            AddedSugarG = meal.Macros.AddedSugarG,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.MealEntry>(pull).FirstOrDefault(m => m.Id == id);
        if (row is null) return;
        row.IsDeleted = true;
        row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static Meal ToDomain(Persistence.MealEntry m) =>
        Meal.Rehydrate(m.Id, m.Time, (MealType)(int)m.MealType, m.Description, m.PortionNote,
            (Satiety)(int)m.Satiety, m.QualityStars,
            new Macros(m.Calories, m.ProteinG, m.CarbsG, m.FatG, m.FiberG, m.SugarG, m.AddedSugarG, m.SodiumMg),
            Tags.FromCsv(m.Tags));
}

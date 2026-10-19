using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Web implementation of <see cref="IDrinkRepository"/> over the sync API.</summary>
public sealed class ApiDrinkRepository : IDrinkRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiDrinkRepository(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    public async Task<Drink?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.DrinkEntry>(pull).FirstOrDefault(d => d.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Drink>> ListAsync(CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        return WebSyncClient.Rows<Persistence.DrinkEntry>(pull).Select(ToDomain).ToList();
    }

    // The item breakdown is out-of-aggregate, so the web edit form stages it here and the next
    // save writes it in the same push (create or update). Without a staged list, saves keep the
    // row's existing breakdown.
    private bool _hasStagedItems;
    private string? _stagedItemsJson;

    public void StageItems(List<Persistence.MealItemPortion>? items)
    {
        _stagedItemsJson = Persistence.MealItemPortion.Serialize(items);
        _hasStagedItems = true;
    }

    public void ClearStagedItems() => _hasStagedItems = false;

    public async Task SaveAsync(Drink drink, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.DrinkEntry>(pull).FirstOrDefault(d => d.Id == drink.Id);
        await _sync.PushAsync(new Persistence.DrinkEntry
        {
            Id = drink.Id,
            Time = drink.Time,
            Description = drink.Description,
            Ounces = drink.Ounces,
            SugarCount = drink.SugarCount,
            Tags = drink.Tags.ToCsv(),
            Calories = drink.Macros.Calories,
            ProteinG = drink.Macros.ProteinG,
            CarbsG = drink.Macros.CarbsG,
            SugarG = drink.Macros.SugarG,
            FatG = drink.Macros.FatG,
            SodiumMg = drink.Macros.SodiumMg,
            FiberG = drink.Macros.FiberG,
            AddedSugarG = drink.Macros.AddedSugarG,
            ItemsJson = _hasStagedItems ? _stagedItemsJson : existing?.ItemsJson,   // out-of-aggregate breakdown
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _hasStagedItems = false;
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.DrinkEntry>(pull).FirstOrDefault(d => d.Id == id);
        if (row is null) return;
        row.IsDeleted = true;
        row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static Drink ToDomain(Persistence.DrinkEntry d) =>
        Drink.Rehydrate(d.Id, d.Time, d.Description, d.Ounces, d.SugarCount,
            new Macros(d.Calories, d.ProteinG, d.CarbsG, d.FatG, d.FiberG, d.SugarG, d.AddedSugarG, d.SodiumMg),
            Tags.FromCsv(d.Tags));
}

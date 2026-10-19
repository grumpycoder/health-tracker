using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Domain.Nutrition;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Web implementation of <see cref="ISavedFoodRepository"/> over the sync API. Same
/// matching rules as the phone's EF repository: trimmed, case-insensitive names, newest row wins.</summary>
public sealed class ApiSavedFoodRepository : ISavedFoodRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiSavedFoodRepository(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    private async Task<List<Persistence.SavedFoodEntry>> RowsAsync() =>
        WebSyncClient.Rows<Persistence.SavedFoodEntry>(await _state.DataAsync());

    public async Task<SavedFood?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = (await RowsAsync()).FirstOrDefault(f => f.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<SavedFood?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var key = (name ?? "").Trim();
        if (key.Length == 0) return null;
        var row = (await RowsAsync())
            .Where(f => string.Equals(f.Name.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<SavedFood>> ListAsync(CancellationToken ct = default) =>
        (await RowsAsync())
            .OrderByDescending(f => f.UpdatedAt)
            .GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => ToDomain(g.First()))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task SaveAsync(SavedFood food, CancellationToken ct = default)
    {
        var existing = (await RowsAsync()).FirstOrDefault(f => f.Id == food.Id);
        await _sync.PushAsync(new Persistence.SavedFoodEntry
        {
            Id = food.Id,
            Name = food.Name,
            ServingNote = food.ServingNote,
            Calories = food.Macros.Calories,
            ProteinG = food.Macros.ProteinG,
            CarbsG = food.Macros.CarbsG,
            SugarG = food.Macros.SugarG,
            AddedSugarG = food.Macros.AddedSugarG,
            FatG = food.Macros.FatG,
            SodiumMg = food.Macros.SodiumMg,
            FiberG = food.Macros.FiberG,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = (await RowsAsync()).FirstOrDefault(f => f.Id == id);
        if (row is null) return;
        row.IsDeleted = true;
        row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static SavedFood ToDomain(Persistence.SavedFoodEntry f) =>
        SavedFood.Rehydrate(f.Id, f.Name, f.ServingNote,
            new Macros(f.Calories, f.ProteinG, f.CarbsG, f.FatG, f.FiberG, f.SugarG, f.AddedSugarG, f.SodiumMg));
}

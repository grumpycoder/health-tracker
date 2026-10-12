using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Nutrition;

/// <summary>EF Core implementation of <see cref="ISavedFoodRepository"/>. Maps the
/// <see cref="SavedFood"/> aggregate to <see cref="Persistence.SavedFoodEntry"/>.</summary>
public sealed class EfSavedFoodRepository : ISavedFoodRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfSavedFoodRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<SavedFood?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.SavedFoods.FirstOrDefaultAsync(f => f.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<SavedFood?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var key = (name ?? "").Trim();
        if (key.Length == 0) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Small table — match in memory so the comparison is case-insensitive on every provider.
        var rows = await db.SavedFoods.ToListAsync(ct);
        var row = rows
            .Where(f => string.Equals(f.Name.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<SavedFood>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.SavedFoods.OrderByDescending(f => f.UpdatedAt).ToListAsync(ct);
        // One entry per name (newest wins) in case two devices created the same food offline.
        return rows
            .GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => ToDomain(g.First()))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveAsync(SavedFood food, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.SavedFoods.FirstOrDefaultAsync(f => f.Id == food.Id, ct);
        if (row is null)
        {
            row = new Persistence.SavedFoodEntry { Id = food.Id };
            db.SavedFoods.Add(row);
        }

        row.Name = food.Name;
        row.ServingNote = food.ServingNote;
        row.Calories = food.Macros.Calories;
        row.ProteinG = food.Macros.ProteinG;
        row.CarbsG = food.Macros.CarbsG;
        row.SugarG = food.Macros.SugarG;
        row.AddedSugarG = food.Macros.AddedSugarG;
        row.FatG = food.Macros.FatG;
        row.SodiumMg = food.Macros.SodiumMg;
        row.FiberG = food.Macros.FiberG;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.SavedFoods.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null) return;
        db.SavedFoods.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static SavedFood ToDomain(Persistence.SavedFoodEntry f) =>
        SavedFood.Rehydrate(f.Id, f.Name, f.ServingNote,
            new Macros(f.Calories, f.ProteinG, f.CarbsG, f.FatG, f.FiberG, f.SugarG, f.AddedSugarG, f.SodiumMg));
}

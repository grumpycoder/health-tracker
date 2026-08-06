using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Nutrition;

/// <summary>
/// EF Core implementation of <see cref="IMealRepository"/>. Maps the <see cref="Meal"/> aggregate
/// (with its <c>Macros</c>/<c>Tags</c> value objects and domain enums) to
/// <see cref="Persistence.MealEntry"/>. Domain enums map by ordinal to the persistence enums.
/// </summary>
public sealed class EfMealRepository : IMealRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfMealRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<Meal?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MealEntries.FirstOrDefaultAsync(m => m.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Meal>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.MealEntries.OrderByDescending(m => m.Time).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(Meal meal, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MealEntries.FirstOrDefaultAsync(m => m.Id == meal.Id, ct);
        if (row is null)
        {
            row = new Persistence.MealEntry { Id = meal.Id };
            db.MealEntries.Add(row);
        }

        row.Time = meal.Time;
        row.MealType = (Persistence.MealType)(int)meal.MealType;
        row.Description = meal.Description ?? "";
        row.PortionNote = meal.PortionNote;
        row.Satiety = (Persistence.Satiety)(int)meal.Satiety;
        row.QualityStars = meal.QualityStars;
        row.Tags = meal.Tags.ToCsv();
        row.Calories = meal.Macros.Calories;
        row.ProteinG = meal.Macros.ProteinG;
        row.CarbsG = meal.Macros.CarbsG;
        row.SugarG = meal.Macros.SugarG;
        row.FatG = meal.Macros.FatG;
        row.SodiumMg = meal.Macros.SodiumMg;
        row.FiberG = meal.Macros.FiberG;
        row.AddedSugarG = meal.Macros.AddedSugarG;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MealEntries.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return;
        db.MealEntries.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static Meal ToDomain(Persistence.MealEntry m) =>
        Meal.Rehydrate(m.Id, m.Time, (FitRecoveryLog.Domain.Nutrition.MealType)(int)m.MealType, m.Description, m.PortionNote,
            (FitRecoveryLog.Domain.Nutrition.Satiety)(int)m.Satiety, m.QualityStars,
            new Macros(m.Calories, m.ProteinG, m.CarbsG, m.FatG, m.FiberG, m.SugarG, m.AddedSugarG, m.SodiumMg),
            Tags.FromCsv(m.Tags));
}

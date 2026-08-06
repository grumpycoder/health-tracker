using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Nutrition;

/// <summary>EF Core implementation of <see cref="IDrinkRepository"/>. Maps the <see cref="Drink"/>
/// aggregate to <see cref="Persistence.DrinkEntry"/>.</summary>
public sealed class EfDrinkRepository : IDrinkRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfDrinkRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<Drink?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DrinkEntries.FirstOrDefaultAsync(d => d.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Drink>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.DrinkEntries.OrderByDescending(d => d.Time).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(Drink drink, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DrinkEntries.FirstOrDefaultAsync(d => d.Id == drink.Id, ct);
        if (row is null)
        {
            row = new Persistence.DrinkEntry { Id = drink.Id };
            db.DrinkEntries.Add(row);
        }

        row.Time = drink.Time;
        row.Description = drink.Description ?? "";
        row.Ounces = drink.Ounces;
        row.SugarCount = drink.SugarCount;
        row.Tags = drink.Tags.ToCsv();
        row.Calories = drink.Macros.Calories;
        row.ProteinG = drink.Macros.ProteinG;
        row.CarbsG = drink.Macros.CarbsG;
        row.SugarG = drink.Macros.SugarG;
        row.FatG = drink.Macros.FatG;
        row.SodiumMg = drink.Macros.SodiumMg;
        row.FiberG = drink.Macros.FiberG;
        row.AddedSugarG = drink.Macros.AddedSugarG;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DrinkEntries.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return;
        db.DrinkEntries.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static Drink ToDomain(Persistence.DrinkEntry d) =>
        Drink.Rehydrate(d.Id, d.Time, d.Description, d.Ounces, d.SugarCount,
            new Macros(d.Calories, d.ProteinG, d.CarbsG, d.FatG, d.FiberG, d.SugarG, d.AddedSugarG, d.SodiumMg),
            Tags.FromCsv(d.Tags));
}

using FitRecoveryLog.Application.Body;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Body;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Body;

/// <summary>
/// EF Core implementation of <see cref="IMeasurementRepository"/>. Maps the <see cref="Measurement"/>
/// aggregate to <see cref="Persistence.BodyMeasurement"/>. <c>PhotoPath</c> is deliberately left
/// untouched — progress photos aren't part of the measurement aggregate, so an update preserves any
/// photo already attached to the row.
/// </summary>
public sealed class EfMeasurementRepository : IMeasurementRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfMeasurementRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<Measurement?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.BodyMeasurements.FirstOrDefaultAsync(m => m.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Measurement>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.BodyMeasurements.OrderByDescending(m => m.Date).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(Measurement m, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.BodyMeasurements.FirstOrDefaultAsync(x => x.Id == m.Id, ct);
        if (row is null)
        {
            row = new Persistence.BodyMeasurement { Id = m.Id };
            db.BodyMeasurements.Add(row);
        }

        row.Date = m.Date;
        row.WeightLbs = m.WeightLbs;
        row.WaistInches = m.WaistInches;
        row.ChestInches = m.ChestInches;
        row.HipsInches = m.HipsInches;
        row.ArmsInches = m.ArmsInches;
        row.ThighsInches = m.ThighsInches;
        row.BodyFatPercent = m.BodyFatPercent;
        row.MuscleMassLbs = m.MuscleMassLbs;
        row.VisceralFat = m.VisceralFat;
        row.BodyWaterPercent = m.BodyWaterPercent;
        row.BasalMetabolicRate = m.BasalMetabolicRate;
        row.MetabolicAge = m.MetabolicAge;
        row.ClothingFitNotes = m.ClothingFitNotes;
        // PhotoPath intentionally not set — it isn't part of the aggregate.

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.BodyMeasurements.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return;
        db.BodyMeasurements.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static Measurement ToDomain(Persistence.BodyMeasurement m) =>
        Measurement.Rehydrate(m.Id, m.Date, m.WeightLbs, m.WaistInches, m.ChestInches, m.HipsInches,
            m.ArmsInches, m.ThighsInches, m.BodyFatPercent, m.MuscleMassLbs, m.VisceralFat, m.BodyWaterPercent,
            m.BasalMetabolicRate, m.MetabolicAge, m.ClothingFitNotes);
}

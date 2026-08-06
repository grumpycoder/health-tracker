using FitRecoveryLog.Application.Meds;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Meds;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Meds;

/// <summary>EF Core implementation of <see cref="IMedicationRepository"/>, mapping the
/// <see cref="MedicationDose"/> aggregate to <see cref="Persistence.MedicationEntry"/>.</summary>
public sealed class EfMedicationRepository : IMedicationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfMedicationRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<MedicationDose?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MedicationEntries.FirstOrDefaultAsync(m => m.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<MedicationDose>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.MedicationEntries.OrderByDescending(m => m.TakenAt).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(MedicationDose d, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MedicationEntries.FirstOrDefaultAsync(m => m.Id == d.Id, ct);
        if (row is null)
        {
            row = new Persistence.MedicationEntry { Id = d.Id };
            db.MedicationEntries.Add(row);
        }

        row.Name = d.Name;
        row.Dose = d.Dose;
        row.Frequency = d.Frequency;
        row.TakenAt = d.TakenAt;
        row.InjectionSite = d.InjectionSite;
        row.ReactionNotes = d.ReactionNotes;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MedicationEntries.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return;
        db.MedicationEntries.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static MedicationDose ToDomain(Persistence.MedicationEntry m) =>
        MedicationDose.Rehydrate(m.Id, m.Name, m.Dose, m.Frequency, m.TakenAt, m.InjectionSite, m.ReactionNotes);
}

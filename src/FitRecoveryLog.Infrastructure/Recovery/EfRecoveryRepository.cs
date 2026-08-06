using FitRecoveryLog.Application.Recovery;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Recovery;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Recovery;

/// <summary>EF Core implementation of <see cref="IRecoveryRepository"/>, mapping the
/// <see cref="RecoveryLog"/> aggregate (Tags value object + SorenessSeverity enum) to
/// <see cref="Persistence.RecoveryEntry"/>.</summary>
public sealed class EfRecoveryRepository : IRecoveryRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfRecoveryRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<RecoveryLog?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RecoveryEntries.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<RecoveryLog>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.RecoveryEntries.OrderByDescending(r => r.Date).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(RecoveryLog r, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RecoveryEntries.FirstOrDefaultAsync(x => x.Id == r.Id, ct);
        if (row is null)
        {
            row = new Persistence.RecoveryEntry { Id = r.Id };
            db.RecoveryEntries.Add(row);
        }

        row.Date = r.Date;
        row.RecoveryRating = r.RecoveryRating;
        row.FatigueRating = r.FatigueRating;
        row.SorenessLocations = r.SorenessLocations.ToCsv();
        row.SorenessSeverity = (Persistence.SorenessSeverity)(int)r.Severity;
        row.Notes = r.Notes;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RecoveryEntries.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return;
        db.RecoveryEntries.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static RecoveryLog ToDomain(Persistence.RecoveryEntry r) =>
        RecoveryLog.Rehydrate(r.Id, r.Date, r.RecoveryRating, r.FatigueRating,
            Tags.FromCsv(r.SorenessLocations),
            (FitRecoveryLog.Domain.Recovery.SorenessSeverity)(int)r.SorenessSeverity, r.Notes);
}

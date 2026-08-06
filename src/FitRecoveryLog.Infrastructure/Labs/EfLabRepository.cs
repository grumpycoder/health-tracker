using FitRecoveryLog.Application.Labs;
using FitRecoveryLog.Domain.Labs;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Labs;

/// <summary>EF Core implementation of <see cref="ILabRepository"/>. The persistence POCO and the
/// domain aggregate share the name <c>LabResult</c>, so the persistence type is referenced through
/// the <c>Persistence</c> alias and the domain type unqualified.</summary>
public sealed class EfLabRepository : ILabRepository
{
    private readonly IDbContextFactory<Persistence.AppDbContext> _factory;

    public EfLabRepository(IDbContextFactory<Persistence.AppDbContext> factory) => _factory = factory;

    public async Task<LabResult?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.LabResults.FirstOrDefaultAsync(l => l.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<LabResult>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.LabResults.OrderByDescending(l => l.Date).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(LabResult l, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.LabResults.FirstOrDefaultAsync(x => x.Id == l.Id, ct);
        if (row is null)
        {
            row = new Persistence.LabResult { Id = l.Id };
            db.LabResults.Add(row);
        }

        row.Date = l.Date;
        row.LabName = l.LabName;
        row.Value = l.Value;
        row.Unit = l.Unit;
        row.Notes = l.Notes;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.LabResults.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (row is null) return;
        db.LabResults.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static LabResult ToDomain(Persistence.LabResult l) =>
        LabResult.Rehydrate(l.Id, l.Date, l.LabName, l.Value, l.Unit, l.Notes);
}

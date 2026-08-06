using FitRecoveryLog.Application.Recovery;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Recovery;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Recovery;

/// <summary>EF Core implementation of <see cref="ISleepRepository"/>, mapping the
/// <see cref="SleepLog"/> aggregate to <see cref="Persistence.SleepEntry"/>.</summary>
public sealed class EfSleepRepository : ISleepRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfSleepRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<SleepLog?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.SleepEntries.FirstOrDefaultAsync(s => s.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<SleepLog>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.SleepEntries.OrderByDescending(s => s.Date).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(SleepLog s, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.SleepEntries.FirstOrDefaultAsync(x => x.Id == s.Id, ct);
        if (row is null)
        {
            row = new Persistence.SleepEntry { Id = s.Id };
            db.SleepEntries.Add(row);
        }

        row.Date = s.Date;
        row.DurationHours = s.DurationHours;
        row.SleepScore = s.Score;
        row.Interruptions = s.Interruptions;
        row.Notes = s.Notes;
        row.ScoreEstimated = s.ScoreEstimated;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.SleepEntries.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return;
        db.SleepEntries.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }

    private static SleepLog ToDomain(Persistence.SleepEntry s) =>
        SleepLog.Rehydrate(s.Id, s.Date, s.DurationHours, s.SleepScore, s.Interruptions, s.Notes, s.ScoreEstimated);
}

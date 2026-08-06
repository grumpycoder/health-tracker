using FitRecoveryLog.Application.Notes;
using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Notes;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Notes;

/// <summary>EF Core implementation of <see cref="INoteRepository"/>, mapping the
/// <see cref="Note"/> aggregate to <see cref="Persistence.NoteEntry"/>.</summary>
public sealed class EfNoteRepository : INoteRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EfNoteRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<Note?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.NoteEntries.FirstOrDefaultAsync(n => n.Id == id, ct);
        return row is null ? null : Note.Rehydrate(row.Id, row.Time, row.Text);
    }

    public async Task<IReadOnlyList<Note>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.NoteEntries.OrderByDescending(n => n.Time).ToListAsync(ct);
        return rows.Select(n => Note.Rehydrate(n.Id, n.Time, n.Text)).ToList();
    }

    public async Task SaveAsync(Note note, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.NoteEntries.FirstOrDefaultAsync(n => n.Id == note.Id, ct);
        if (row is null)
        {
            row = new Persistence.NoteEntry { Id = note.Id };
            db.NoteEntries.Add(row);
        }

        row.Time = note.Time;
        row.Text = note.Text;

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.NoteEntries.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (row is null) return;
        db.NoteEntries.Remove(row); // AppDbContext converts this to a tombstone
        await db.SaveChangesAsync(ct);
    }
}

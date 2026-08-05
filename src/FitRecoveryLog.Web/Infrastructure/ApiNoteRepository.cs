using FitRecoveryLog.Application.Notes;
using FitRecoveryLog.Domain.Notes;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

public sealed class ApiNoteRepository : INoteRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;
    public ApiNoteRepository(AppState state, WebSyncClient sync) { _state = state; _sync = sync; }

    public async Task<Note?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.NoteEntry>(await _state.DataAsync()).FirstOrDefault(n => n.Id == id);
        return row is null ? null : Note.Rehydrate(row.Id, row.Time, row.Text);
    }

    public async Task<IReadOnlyList<Note>> ListAsync(CancellationToken ct = default) =>
        WebSyncClient.Rows<Persistence.NoteEntry>(await _state.DataAsync())
            .Select(n => Note.Rehydrate(n.Id, n.Time, n.Text)).ToList();

    public async Task SaveAsync(Note note, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.NoteEntry>(pull).FirstOrDefault(x => x.Id == note.Id);
        await _sync.PushAsync(new Persistence.NoteEntry
        {
            Id = note.Id, Time = note.Time, Text = note.Text,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.NoteEntry>(await _state.DataAsync()).FirstOrDefault(n => n.Id == id);
        if (row is null) return;
        row.IsDeleted = true; row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }
}

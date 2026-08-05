using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Notes;

namespace FitRecoveryLog.Application.Notes;

public interface INoteRepository
{
    Task<Note?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(Note note, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

public sealed class NoteService
{
    private readonly INoteRepository _repo;
    public NoteService(INoteRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Note>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Result<Guid>> AddAsync(DateTime time, string text, CancellationToken ct = default)
    {
        Note note;
        try { note = Note.Create(time, text); }
        catch (ArgumentException ex) { return Result<Guid>.Failure(ex.Message); }
        await _repo.SaveAsync(note, ct);
        return Result<Guid>.Success(note.Id);
    }

    public async Task<Result> EditAsync(Guid id, DateTime time, string text, CancellationToken ct = default)
    {
        var note = await _repo.GetAsync(id, ct);
        if (note is null) return Result.Failure("Note not found.");
        try { note.SetTime(time); note.SetText(text); }
        catch (ArgumentException ex) { return Result.Failure(ex.Message); }
        await _repo.SaveAsync(note, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var note = await _repo.GetAsync(id, ct);
        if (note is null) return Result.Failure("Note not found.");
        await _repo.RemoveAsync(id, ct);
        return Result.Success();
    }
}

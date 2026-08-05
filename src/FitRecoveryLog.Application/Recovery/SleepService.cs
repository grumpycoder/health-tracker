using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Recovery;

namespace FitRecoveryLog.Application.Recovery;

public interface ISleepRepository
{
    Task<SleepLog?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SleepLog>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(SleepLog sleep, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

public sealed record SleepData(DateOnly Date, double? DurationHours, int? Score, int? Interruptions, string? Notes);

public sealed class SleepService
{
    private readonly ISleepRepository _repo;
    public SleepService(ISleepRepository repo) => _repo = repo;

    public Task<IReadOnlyList<SleepLog>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(SleepData d, CancellationToken ct = default)
    {
        var s = SleepLog.Create(d.Date);
        try { Apply(s, d); } catch (ArgumentOutOfRangeException ex) { return Result<Guid>.Failure(ex.Message); }
        await _repo.SaveAsync(s, ct);
        return Result<Guid>.Success(s.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, SleepData d, CancellationToken ct = default)
    {
        var s = await _repo.GetAsync(id, ct);
        if (s is null) return Result.Failure("Sleep entry not found.");
        try { Apply(s, d); } catch (ArgumentOutOfRangeException ex) { return Result.Failure(ex.Message); }
        await _repo.SaveAsync(s, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _repo.GetAsync(id, ct);
        if (s is null) return Result.Failure("Sleep entry not found.");
        await _repo.RemoveAsync(id, ct);
        return Result.Success();
    }

    private static void Apply(SleepLog s, SleepData d)
    {
        s.SetDate(d.Date);
        s.SetDuration(d.DurationHours);
        s.SetInterruptions(d.Interruptions);
        s.SetNotes(d.Notes);
        s.SetScore(d.Score, estimated: false); // a hand-entered score is a real one
    }
}

using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Recovery;

namespace FitRecoveryLog.Application.Recovery;

public interface IRecoveryRepository
{
    Task<RecoveryLog?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RecoveryLog>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(RecoveryLog recovery, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

public sealed record RecoveryData(DateOnly Date, int? RecoveryRating, int? FatigueRating,
    Tags SorenessLocations, SorenessSeverity Severity, string? Notes);

public sealed class RecoveryService
{
    private readonly IRecoveryRepository _repo;
    public RecoveryService(IRecoveryRepository repo) => _repo = repo;

    public Task<IReadOnlyList<RecoveryLog>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(RecoveryData d, CancellationToken ct = default)
    {
        var r = RecoveryLog.Create(d.Date);
        Apply(r, d);
        await _repo.SaveAsync(r, ct);
        return Result<Guid>.Success(r.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, RecoveryData d, CancellationToken ct = default)
    {
        var r = await _repo.GetAsync(id, ct);
        if (r is null) return Result.Failure("Recovery entry not found.");
        Apply(r, d);
        await _repo.SaveAsync(r, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _repo.GetAsync(id, ct);
        if (r is null) return Result.Failure("Recovery entry not found.");
        await _repo.RemoveAsync(id, ct);
        return Result.Success();
    }

    private static void Apply(RecoveryLog r, RecoveryData d)
    {
        r.SetDate(d.Date);
        r.SetRecoveryRating(d.RecoveryRating);
        r.SetFatigueRating(d.FatigueRating);
        r.SetSoreness(d.SorenessLocations, d.Severity);
        r.SetNotes(d.Notes);
    }
}

using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Labs;

namespace FitRecoveryLog.Application.Labs;

public interface ILabRepository
{
    Task<LabResult?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LabResult>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(LabResult lab, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

public sealed record LabData(DateOnly Date, string LabName, double? Value, string? Unit, string? Notes);

public sealed class LabService
{
    private readonly ILabRepository _repo;
    public LabService(ILabRepository repo) => _repo = repo;

    public Task<IReadOnlyList<LabResult>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(LabData d, CancellationToken ct = default)
    {
        LabResult lab;
        try { lab = LabResult.Create(d.Date, d.LabName); Apply(lab, d); }
        catch (ArgumentException ex) { return Result<Guid>.Failure(ex.Message); }
        await _repo.SaveAsync(lab, ct);
        return Result<Guid>.Success(lab.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, LabData d, CancellationToken ct = default)
    {
        var lab = await _repo.GetAsync(id, ct);
        if (lab is null) return Result.Failure("Lab result not found.");
        try { Apply(lab, d); } catch (ArgumentException ex) { return Result.Failure(ex.Message); }
        await _repo.SaveAsync(lab, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var lab = await _repo.GetAsync(id, ct);
        if (lab is null) return Result.Failure("Lab result not found.");
        await _repo.RemoveAsync(id, ct);
        return Result.Success();
    }

    private static void Apply(LabResult lab, LabData d)
    {
        lab.SetLabName(d.LabName);
        lab.SetDate(d.Date);
        lab.SetValue(d.Value);
        lab.SetUnit(d.Unit);
        lab.SetNotes(d.Notes);
    }
}

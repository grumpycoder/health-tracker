using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Body;

namespace FitRecoveryLog.Application.Body;

public interface IMeasurementRepository
{
    Task<Measurement?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Measurement>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(Measurement measurement, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

public sealed record MeasurementData(DateOnly Date, double? WeightLbs, double? WaistInches, double? ChestInches,
    double? HipsInches, double? ArmsInches, double? ThighsInches, double? BodyFatPercent, double? MuscleMassLbs,
    double? VisceralFat, double? BodyWaterPercent, int? BasalMetabolicRate, int? MetabolicAge, string? ClothingFitNotes);

public sealed class MeasurementService
{
    private readonly IMeasurementRepository _repo;
    public MeasurementService(IMeasurementRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Measurement>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(MeasurementData d, CancellationToken ct = default)
    {
        var m = Measurement.Create(d.Date);
        try { Apply(m, d); } catch (ArgumentOutOfRangeException ex) { return Result<Guid>.Failure(ex.Message); }
        await _repo.SaveAsync(m, ct);
        return Result<Guid>.Success(m.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, MeasurementData d, CancellationToken ct = default)
    {
        var m = await _repo.GetAsync(id, ct);
        if (m is null) return Result.Failure("Measurement not found.");
        try { Apply(m, d); } catch (ArgumentOutOfRangeException ex) { return Result.Failure(ex.Message); }
        await _repo.SaveAsync(m, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var m = await _repo.GetAsync(id, ct);
        if (m is null) return Result.Failure("Measurement not found.");
        await _repo.RemoveAsync(id, ct);
        return Result.Success();
    }

    private static void Apply(Measurement m, MeasurementData d)
    {
        m.SetDate(d.Date);
        m.Update(d.WeightLbs, d.WaistInches, d.ChestInches, d.HipsInches, d.ArmsInches, d.ThighsInches,
            d.BodyFatPercent, d.MuscleMassLbs, d.VisceralFat, d.BodyWaterPercent, d.BasalMetabolicRate,
            d.MetabolicAge, d.ClothingFitNotes);
    }
}

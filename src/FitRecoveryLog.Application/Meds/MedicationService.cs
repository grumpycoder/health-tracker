using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Meds;

namespace FitRecoveryLog.Application.Meds;

public interface IMedicationRepository
{
    Task<MedicationDose?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MedicationDose>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(MedicationDose dose, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

public sealed record MedicationDoseData(DateTime TakenAt, string Name, string? Dose, string? Frequency, string? InjectionSite, string? ReactionNotes);

public sealed class MedicationService
{
    private readonly IMedicationRepository _repo;
    public MedicationService(IMedicationRepository repo) => _repo = repo;

    public Task<IReadOnlyList<MedicationDose>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(MedicationDoseData d, CancellationToken ct = default)
    {
        MedicationDose dose;
        try { dose = MedicationDose.Create(d.TakenAt, d.Name); Apply(dose, d); }
        catch (ArgumentException ex) { return Result<Guid>.Failure(ex.Message); }
        await _repo.SaveAsync(dose, ct);
        return Result<Guid>.Success(dose.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, MedicationDoseData d, CancellationToken ct = default)
    {
        var dose = await _repo.GetAsync(id, ct);
        if (dose is null) return Result.Failure("Dose not found.");
        try { Apply(dose, d); } catch (ArgumentException ex) { return Result.Failure(ex.Message); }
        await _repo.SaveAsync(dose, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var dose = await _repo.GetAsync(id, ct);
        if (dose is null) return Result.Failure("Dose not found.");
        await _repo.RemoveAsync(id, ct);
        return Result.Success();
    }

    private static void Apply(MedicationDose dose, MedicationDoseData d)
    {
        dose.SetName(d.Name);
        dose.SetTakenAt(d.TakenAt);
        dose.SetDose(d.Dose);
        dose.SetFrequency(d.Frequency);
        dose.SetInjectionSite(d.InjectionSite);
        dose.SetReactionNotes(d.ReactionNotes);
    }
}

using FitRecoveryLog.Application.Meds;
using FitRecoveryLog.Domain.Meds;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

public sealed class ApiMedicationRepository : IMedicationRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;
    public ApiMedicationRepository(AppState state, WebSyncClient sync) { _state = state; _sync = sync; }

    public async Task<MedicationDose?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.MedicationEntry>(await _state.DataAsync()).FirstOrDefault(m => m.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<MedicationDose>> ListAsync(CancellationToken ct = default) =>
        WebSyncClient.Rows<Persistence.MedicationEntry>(await _state.DataAsync()).Select(ToDomain).ToList();

    public async Task SaveAsync(MedicationDose d, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.MedicationEntry>(pull).FirstOrDefault(x => x.Id == d.Id);
        await _sync.PushAsync(new Persistence.MedicationEntry
        {
            Id = d.Id, Name = d.Name, Dose = d.Dose, Frequency = d.Frequency, TakenAt = d.TakenAt,
            InjectionSite = d.InjectionSite, ReactionNotes = d.ReactionNotes,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.MedicationEntry>(await _state.DataAsync()).FirstOrDefault(m => m.Id == id);
        if (row is null) return;
        row.IsDeleted = true; row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static MedicationDose ToDomain(Persistence.MedicationEntry m) =>
        MedicationDose.Rehydrate(m.Id, m.Name, m.Dose, m.Frequency, m.TakenAt, m.InjectionSite, m.ReactionNotes);
}

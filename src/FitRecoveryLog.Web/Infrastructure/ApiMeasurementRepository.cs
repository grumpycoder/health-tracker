using FitRecoveryLog.Application.Body;
using FitRecoveryLog.Domain.Body;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

public sealed class ApiMeasurementRepository : IMeasurementRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;
    public ApiMeasurementRepository(AppState state, WebSyncClient sync) { _state = state; _sync = sync; }

    public async Task<Measurement?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.BodyMeasurement>(await _state.DataAsync()).FirstOrDefault(m => m.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Measurement>> ListAsync(CancellationToken ct = default) =>
        WebSyncClient.Rows<Persistence.BodyMeasurement>(await _state.DataAsync()).Select(ToDomain).ToList();

    public async Task SaveAsync(Measurement m, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.BodyMeasurement>(pull).FirstOrDefault(x => x.Id == m.Id);
        await _sync.PushAsync(new Persistence.BodyMeasurement
        {
            Id = m.Id, Date = m.Date, WeightLbs = m.WeightLbs, WaistInches = m.WaistInches,
            ChestInches = m.ChestInches, HipsInches = m.HipsInches, ArmsInches = m.ArmsInches,
            ThighsInches = m.ThighsInches, BodyFatPercent = m.BodyFatPercent, MuscleMassLbs = m.MuscleMassLbs,
            VisceralFat = m.VisceralFat, BodyWaterPercent = m.BodyWaterPercent, BasalMetabolicRate = m.BasalMetabolicRate,
            MetabolicAge = m.MetabolicAge, ClothingFitNotes = m.ClothingFitNotes,
            PhotoPath = existing?.PhotoPath, CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.BodyMeasurement>(await _state.DataAsync()).FirstOrDefault(m => m.Id == id);
        if (row is null) return;
        row.IsDeleted = true; row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static Measurement ToDomain(Persistence.BodyMeasurement m) =>
        Measurement.Rehydrate(m.Id, m.Date, m.WeightLbs, m.WaistInches, m.ChestInches, m.HipsInches,
            m.ArmsInches, m.ThighsInches, m.BodyFatPercent, m.MuscleMassLbs, m.VisceralFat, m.BodyWaterPercent,
            m.BasalMetabolicRate, m.MetabolicAge, m.ClothingFitNotes);
}

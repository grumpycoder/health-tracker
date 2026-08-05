using FitRecoveryLog.Application.Recovery;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Recovery;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

public sealed class ApiRecoveryRepository : IRecoveryRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;
    public ApiRecoveryRepository(AppState state, WebSyncClient sync) { _state = state; _sync = sync; }

    public async Task<RecoveryLog?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.RecoveryEntry>(await _state.DataAsync()).FirstOrDefault(r => r.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<RecoveryLog>> ListAsync(CancellationToken ct = default) =>
        WebSyncClient.Rows<Persistence.RecoveryEntry>(await _state.DataAsync()).Select(ToDomain).ToList();

    public async Task SaveAsync(RecoveryLog r, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.RecoveryEntry>(pull).FirstOrDefault(x => x.Id == r.Id);
        await _sync.PushAsync(new Persistence.RecoveryEntry
        {
            Id = r.Id, Date = r.Date, RecoveryRating = r.RecoveryRating, FatigueRating = r.FatigueRating,
            SorenessLocations = r.SorenessLocations.ToCsv(),
            SorenessSeverity = (Persistence.SorenessSeverity)(int)r.Severity,
            Notes = r.Notes, CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.RecoveryEntry>(await _state.DataAsync()).FirstOrDefault(r => r.Id == id);
        if (row is null) return;
        row.IsDeleted = true; row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static RecoveryLog ToDomain(Persistence.RecoveryEntry r) =>
        RecoveryLog.Rehydrate(r.Id, r.Date, r.RecoveryRating, r.FatigueRating,
            Tags.FromCsv(r.SorenessLocations), (SorenessSeverity)(int)r.SorenessSeverity, r.Notes);
}

using FitRecoveryLog.Application.Labs;
using FitRecoveryLog.Domain.Labs;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

public sealed class ApiLabRepository : ILabRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;
    public ApiLabRepository(AppState state, WebSyncClient sync) { _state = state; _sync = sync; }

    public async Task<LabResult?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.LabResult>(await _state.DataAsync()).FirstOrDefault(l => l.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<LabResult>> ListAsync(CancellationToken ct = default) =>
        WebSyncClient.Rows<Persistence.LabResult>(await _state.DataAsync()).Select(ToDomain).ToList();

    public async Task SaveAsync(LabResult l, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.LabResult>(pull).FirstOrDefault(x => x.Id == l.Id);
        await _sync.PushAsync(new Persistence.LabResult
        {
            Id = l.Id, Date = l.Date, LabName = l.LabName, Value = l.Value, Unit = l.Unit, Notes = l.Notes,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.LabResult>(await _state.DataAsync()).FirstOrDefault(l => l.Id == id);
        if (row is null) return;
        row.IsDeleted = true; row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static LabResult ToDomain(Persistence.LabResult l) =>
        LabResult.Rehydrate(l.Id, l.Date, l.LabName, l.Value, l.Unit, l.Notes);
}

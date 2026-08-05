using FitRecoveryLog.Application.Recovery;
using FitRecoveryLog.Domain.Recovery;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

public sealed class ApiSleepRepository : ISleepRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;
    public ApiSleepRepository(AppState state, WebSyncClient sync) { _state = state; _sync = sync; }

    public async Task<SleepLog?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.SleepEntry>(await _state.DataAsync()).FirstOrDefault(s => s.Id == id);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<SleepLog>> ListAsync(CancellationToken ct = default) =>
        WebSyncClient.Rows<Persistence.SleepEntry>(await _state.DataAsync()).Select(ToDomain).ToList();

    public async Task SaveAsync(SleepLog s, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.SleepEntry>(pull).FirstOrDefault(x => x.Id == s.Id);
        await _sync.PushAsync(new Persistence.SleepEntry
        {
            Id = s.Id, Date = s.Date, DurationHours = s.DurationHours, SleepScore = s.Score,
            Interruptions = s.Interruptions, Notes = s.Notes, ScoreEstimated = s.ScoreEstimated,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = WebSyncClient.Rows<Persistence.SleepEntry>(await _state.DataAsync()).FirstOrDefault(s => s.Id == id);
        if (row is null) return;
        row.IsDeleted = true; row.DeletedAt = DateTime.UtcNow;
        await _sync.PushAsync(row);
        _state.Invalidate();
    }

    private static SleepLog ToDomain(Persistence.SleepEntry s) =>
        SleepLog.Rehydrate(s.Id, s.Date, s.DurationHours, s.SleepScore, s.Interruptions, s.Notes, s.ScoreEstimated);
}

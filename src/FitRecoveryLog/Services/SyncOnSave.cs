using FitRecoveryLog.Data;
using FitRecoveryLog.Infrastructure.Sync;

namespace FitRecoveryLog.Services;

/// <summary>Pushes local edits a few seconds after they're saved, so the web and other devices see
/// a meal right away instead of after the next launch/foreground sync. Saves in quick succession
/// collapse into one sync; a save that lands while a sync is running gets a follow-up sync.</summary>
public sealed class SyncOnSave
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    private readonly CloudSyncService _sync;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;

    public SyncOnSave(CloudSyncService sync) => _sync = sync;

    public void Start() => AppDbContext.LocalChangesSaved += Schedule;

    private void Schedule()
    {
        CancellationToken token;
        lock (_gate)
        {
            _pending?.Cancel();
            _pending = new CancellationTokenSource();
            token = _pending.Token;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Delay, token);
                // Another sync in flight: wait for it, then run ours so this save isn't skipped.
                while (_sync.IsSyncing) await Task.Delay(TimeSpan.FromSeconds(2), token);
                await _sync.SyncAsync(allowInteractive: false, token);
            }
            catch (OperationCanceledException) { /* superseded by a newer save */ }
            catch { /* best-effort; the foreground sync retries */ }
        });
    }
}

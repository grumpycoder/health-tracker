namespace FitRecoveryLog.Application.Sync;

/// <summary>
/// Persists the sync engine's cursors and last-sync time. Behind a port so the engine can live
/// in Infrastructure (shared by any client) while the actual storage stays platform-specific —
/// the phone backs it with Preferences; a desktop client would use its own store.
/// </summary>
public interface ISyncStateStore
{
    /// <summary>Highest local UpdatedAt already pushed (local clock).</summary>
    long PushCursor { get; set; }

    /// <summary>Highest server UpdatedAt seen (server clock).</summary>
    long PullCursor { get; set; }

    /// <summary>When the last successful sync completed, or null if never.</summary>
    DateTime? LastSyncUtc { get; set; }
}

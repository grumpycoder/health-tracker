using FitRecoveryLog.Application.Sync;
using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

/// <summary>Phone <see cref="ISyncStateStore"/> backed by Preferences. Uses the same keys the
/// sync engine used before this moved to Infrastructure, so existing cursors carry over.</summary>
public sealed class PreferencesSyncStateStore : ISyncStateStore
{
    public long PushCursor
    {
        get => Preferences.Default.Get("sync.pushCursor", 0L);
        set => Preferences.Default.Set("sync.pushCursor", value);
    }

    public long PullCursor
    {
        get => Preferences.Default.Get("sync.pullCursor", 0L);
        set => Preferences.Default.Set("sync.pullCursor", value);
    }

    public DateTime? LastSyncUtc
    {
        get { var t = Preferences.Default.Get("sync.lastUtc", 0L); return t == 0 ? null : new DateTime(t, DateTimeKind.Utc); }
        set => Preferences.Default.Set("sync.lastUtc", value?.Ticks ?? 0L);
    }
}

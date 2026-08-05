using System.Text.Json;

namespace FitRecoveryLog.Data.Sync;

/// <summary>
/// Wire contracts for the cloud sync API (<c>/api/v1/sync</c>), shared so the phone client
/// and any future web client speak the same shapes. Rows are carried as raw JSON keyed by
/// entity type name (e.g. "MealEntry") so adding an entity type later needs no protocol
/// change. These are wire-compatible with the server's response shapes (System.Text.Json
/// web defaults / camelCase).
/// </summary>
public sealed class SyncPushRequest
{
    public Dictionary<string, List<JsonElement>> Changes { get; set; } = new();
}

public sealed class SyncPullResponse
{
    /// <summary>Server cursor (UTC ticks) to persist and send as <c>?since=</c> next pull.</summary>
    public long Cursor { get; set; }

    /// <summary>entityTypeName -> rows (each includes IsDeleted, so tombstones arrive here too).</summary>
    public Dictionary<string, List<JsonElement>> Changes { get; set; } = new();

    /// <summary>Server signals the client's cursor is too old (post tombstone-purge) and it
    /// must do a full resync. Always false today; handled defensively from day one.</summary>
    public bool FullResyncRequired { get; set; }
}

public sealed class SyncPushResponse
{
    public int Applied { get; set; }
    public long Cursor { get; set; }
}

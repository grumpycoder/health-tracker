using System.Text.Json;

namespace FitRecoveryLog.Server.Sync;

/// <summary>
/// Generic, entity-keyed change envelope (push). Keyed by entity type name
/// (e.g. "MealEntry" -> the rows changed locally since the client's last push).
/// Rows are raw JSON so adding a new entity type later needs no protocol change —
/// unknown keys are ignored by the server (forward-compatible).
/// </summary>
public sealed class SyncPushRequest
{
    public Dictionary<string, List<JsonElement>> Changes { get; set; } = new();
}

/// <summary>Server's response to a pull: everything newer than the client's cursor.</summary>
public sealed class SyncPullResponse
{
    /// <summary>New cursor = max <c>UpdatedAt</c> (UTC ticks) in this batch, or the
    /// incoming cursor if nothing changed. The client persists it for next time.</summary>
    public long Cursor { get; set; }

    /// <summary>entityTypeName -> rows (each row includes IsDeleted, so tombstones
    /// propagate through the same channel as live rows).</summary>
    public Dictionary<string, List<object>> Changes { get; set; } = new();

    /// <summary>Set when the client's cursor is older than the server's retained history
    /// (post tombstone-purge) and it must fall back to a full resync. Always false today
    /// (no purge yet) — reserved so clients can handle it from day one.</summary>
    public bool FullResyncRequired { get; set; }
}

public sealed class SyncPushResponse
{
    public int Applied { get; set; }

    /// <summary>Server's current max cursor after applying the push. The client advances
    /// its cursor to this so it doesn't re-pull the rows it just pushed.</summary>
    public long Cursor { get; set; }
}

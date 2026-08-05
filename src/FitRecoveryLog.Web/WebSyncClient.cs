using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FitRecoveryLog.Data;
using FitRecoveryLog.Data.Sync;

namespace FitRecoveryLog.Web;

/// <summary>API endpoints/identity for the sync API (public identifiers, not secrets).</summary>
public static class SyncApi
{
    public const string BaseUrl = "https://fitlog-api-b6c3yia2b5u3g.azurewebsites.net";
    public const string Scope = "api://3688a639-d636-4e0c-94be-cab645dd5927/access_as_user";
}

/// <summary>
/// The web client is online-only: it reads by pulling everything from the cloud and writes
/// by pushing changed rows — the same <c>/api/v1/sync</c> API the phone uses, no local store.
/// </summary>
public sealed class WebSyncClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    private readonly HttpClient _http;
    public WebSyncClient(HttpClient http) => _http = http;

    /// <summary>Pull the full dataset from the cloud (since=0).</summary>
    public async Task<SyncPullResponse> PullAllAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<SyncPullResponse>("/api/v1/sync?since=0", JsonOpts, ct) ?? new();

    /// <summary>Deserialize one entity type's live (non-tombstoned) rows from a pull.</summary>
    public static List<T> Rows<T>(SyncPullResponse pull) where T : EntityBase
    {
        if (!pull.Changes.TryGetValue(typeof(T).Name, out var list)) return new();
        return list.Select(e => e.Deserialize<T>(JsonOpts)!)
                   .Where(x => x is not null && !x.IsDeleted)
                   .ToList();
    }

    /// <summary>Deep-copy an entity (for editing a draft without mutating the cached row).</summary>
    public static T Clone<T>(T entity) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToElement(entity, JsonOpts), JsonOpts)!;

    /// <summary>Push a single new/changed entity to the cloud (upsert by Id).</summary>
    public Task PushAsync<T>(T entity, CancellationToken ct = default) where T : EntityBase =>
        PushAsync(new[] { entity }, ct);

    /// <summary>Push a batch of same-typed new/changed entities (upsert by Id).</summary>
    public async Task PushAsync<T>(IReadOnlyCollection<T> entities, CancellationToken ct = default) where T : EntityBase
    {
        if (entities.Count == 0) return;
        var req = new SyncPushRequest();
        req.Changes[typeof(T).Name] = entities.Select(e => JsonSerializer.SerializeToElement((object)e, JsonOpts)).ToList();
        var res = await _http.PostAsJsonAsync("/api/v1/sync", req, JsonOpts, ct);
        res.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Session-lived cache of the last full pull so navigating between pages doesn't re-fetch
/// the whole dataset each time. Invalidated after a write (or force-refreshed).
/// </summary>
public sealed class AppState
{
    private readonly WebSyncClient _sync;
    private SyncPullResponse? _pull;

    public AppState(WebSyncClient sync) => _sync = sync;

    public async Task<SyncPullResponse> DataAsync(bool refresh = false)
    {
        if (_pull is null || refresh) _pull = await _sync.PullAllAsync();
        return _pull;
    }

    public void Invalidate() => _pull = null;
}

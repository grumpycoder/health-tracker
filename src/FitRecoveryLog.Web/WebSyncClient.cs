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

    /// <summary>Push a single new/changed entity to the cloud (upsert by Id).</summary>
    public async Task PushAsync<T>(T entity, CancellationToken ct = default) where T : EntityBase
    {
        var req = new SyncPushRequest();
        req.Changes[typeof(T).Name] = new() { JsonSerializer.SerializeToElement((object)entity, JsonOpts) };
        var res = await _http.PostAsJsonAsync("/api/v1/sync", req, JsonOpts, ct);
        res.EnsureSuccessStatusCode();
    }
}

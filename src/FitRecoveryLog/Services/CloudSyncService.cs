using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FitRecoveryLog.Data;
using FitRecoveryLog.Data.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

public sealed record SyncResult(bool Success, int Pushed, int Pulled, string? Error)
{
    public static SyncResult Ok(int pushed, int pulled) => new(true, pushed, pulled, null);
    public static SyncResult Fail(string error) => new(false, 0, 0, error);
}

/// <summary>
/// Local-first sync client: pushes locally-changed rows to the cloud, then pulls newer
/// rows down, over <c>/api/v1/sync</c>. Entity-agnostic (discovers types from the model;
/// reflection only supplies the type argument — the LINQ/JSON is compile-checked).
///
/// Cursors (v1, matching the server's UpdatedAt cursor):
///  - <b>pull cursor</b> = highest server UpdatedAt seen (server clock).
///  - <b>push cursor</b> = highest local UpdatedAt already pushed (local clock).
/// Applied rows are written with <see cref="AppDbContext.SuppressTimestamps"/> so they keep
/// the server's UpdatedAt (no local re-stamp), and the push cursor is advanced past them so
/// server-originated rows don't echo back. Clock-skew edge is the documented v1 caveat
/// (a server row-version cursor is the later hardening upgrade).
/// </summary>
public sealed class CloudSyncService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    private const string PushKey = "sync.pushCursor";
    private const string PullKey = "sync.pullCursor";
    private const string LastKey = "sync.lastUtc";

    private static readonly MethodInfo CollectMi =
        typeof(CloudSyncService).GetMethod(nameof(CollectAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo ApplyMi =
        typeof(CloudSyncService).GetMethod(nameof(ApplyAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IAccessTokenProvider _auth;
    private readonly HttpClient _http;

    public event Action? StateChanged;
    public bool IsSyncing { get; private set; }

    public DateTime? LastSyncUtc
    {
        get { var t = Preferences.Default.Get(LastKey, 0L); return t == 0 ? null : new DateTime(t, DateTimeKind.Utc); }
    }

    public CloudSyncService(IDbContextFactory<AppDbContext> factory, IAccessTokenProvider auth)
    {
        _factory = factory;
        _auth = auth;
        // Generous timeout for the first full sync (thousands of rows in one push/pull);
        // steady-state syncs are tiny.
        _http = new HttpClient { BaseAddress = new Uri(SyncConfig.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(180) };
    }

    private sealed class Box { public long V; }

    public async Task<SyncResult> SyncAsync(bool allowInteractive, CancellationToken ct = default)
    {
        if (IsSyncing) return SyncResult.Fail("Sync already in progress.");
        IsSyncing = true;
        StateChanged?.Invoke();
        try
        {
            var token = await _auth.GetTokenAsync(allowInteractive, ct);
            if (string.IsNullOrEmpty(token)) return SyncResult.Fail("Not signed in.");

            var pushed = await PushAsync(token, ct);
            var pulled = await PullAsync(token, ct);

            Preferences.Default.Set(LastKey, DateTime.UtcNow.Ticks);
            return SyncResult.Ok(pushed, pulled);
        }
        catch (Exception ex)
        {
            return SyncResult.Fail(ex.Message);
        }
        finally
        {
            IsSyncing = false;
            StateChanged?.Invoke();
        }
    }

    // ---- push ----

    private async Task<int> PushAsync(string token, CancellationToken ct)
    {
        long pushCursor = Preferences.Default.Get(PushKey, 0L);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var since = TicksToUtc(pushCursor);
        var req = new SyncPushRequest();
        var max = new Box { V = pushCursor };

        foreach (var t in EntityTypes(db))
            await (Task)CollectMi.MakeGenericMethod(t).Invoke(this, new object[] { db, since, req, max })!;

        if (req.Changes.Count == 0) return 0;

        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sync")
        {
            Content = JsonContent.Create(req, options: JsonOpts),
        };
        msg.Headers.Authorization = new("Bearer", token);
        using var res = await _http.SendAsync(msg, ct);
        res.EnsureSuccessStatusCode();
        var pr = await res.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOpts, ct);

        Preferences.Default.Set(PushKey, max.V);
        return pr?.Applied ?? req.Changes.Sum(c => c.Value.Count);
    }

    private async Task CollectAsync<T>(AppDbContext db, DateTime since, SyncPushRequest req, Box max) where T : EntityBase
    {
        var rows = await db.Set<T>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.UpdatedAt > since)
            .OrderBy(e => e.UpdatedAt)
            .ToListAsync();
        if (rows.Count == 0) return;

        req.Changes[typeof(T).Name] = rows.Select(r => JsonSerializer.SerializeToElement((object)r, JsonOpts)).ToList();
        var m = rows.Max(r => r.UpdatedAt.Ticks);
        if (m > max.V) max.V = m;
    }

    // ---- pull ----

    private async Task<int> PullAsync(string token, CancellationToken ct)
    {
        long pullCursor = Preferences.Default.Get(PullKey, 0L);

        using var msg = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/sync?since={pullCursor}");
        msg.Headers.Authorization = new("Bearer", token);
        using var res = await _http.SendAsync(msg, ct);
        res.EnsureSuccessStatusCode();
        var resp = await res.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOpts, ct) ?? new();

        if (resp.FullResyncRequired)
        {
            // Cursor too old (post-purge). Reset so the next pull starts from scratch.
            Preferences.Default.Set(PullKey, 0L);
            return 0;
        }

        var pulled = 0;
        if (resp.Changes.Count > 0)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            db.SuppressTimestamps = true; // write server rows verbatim (keep server UpdatedAt)
            var map = EntityTypes(db).ToDictionary(t => t.Name);
            var maxApplied = new Box { V = Preferences.Default.Get(PushKey, 0L) };

            foreach (var (name, rows) in resp.Changes)
            {
                if (!map.TryGetValue(name, out var t)) continue; // ignore unknown types
                pulled += rows.Count;
                await (Task)ApplyMi.MakeGenericMethod(t).Invoke(this, new object[] { db, rows, maxApplied })!;
            }

            await db.SaveChangesAsync(ct);
            // Applied rows carry server UpdatedAt; advance the push cursor past them so they
            // aren't seen as local edits and pushed straight back.
            Preferences.Default.Set(PushKey, maxApplied.V);
        }

        Preferences.Default.Set(PullKey, resp.Cursor);
        return pulled;
    }

    private async Task ApplyAsync<T>(AppDbContext db, List<JsonElement> rows, Box maxApplied) where T : EntityBase
    {
        var incoming = rows.Select(el => el.Deserialize<T>(JsonOpts)).OfType<T>().ToList();
        if (incoming.Count == 0) return;

        // Load this table's existing rows once (local SQLite tables are small) and upsert in
        // memory — a query per row would mean thousands of round-trips on the first full sync.
        var existing = await db.Set<T>().IgnoreQueryFilters().ToDictionaryAsync(e => e.Id);

        foreach (var row in incoming)
        {
            if (existing.TryGetValue(row.Id, out var cur))
                db.Entry(cur).CurrentValues.SetValues(row);
            else
                db.Add(row);

            if (row.UpdatedAt.Ticks > maxApplied.V) maxApplied.V = row.UpdatedAt.Ticks;
        }
    }

    // ---- helpers ----

    private static IEnumerable<Type> EntityTypes(DbContext db) =>
        db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => typeof(EntityBase).IsAssignableFrom(t))
            .Distinct();

    private static DateTime TicksToUtc(long ticks) =>
        ticks <= 0 ? DateTime.MinValue.ToUniversalTime() : new DateTime(ticks, DateTimeKind.Utc);
}

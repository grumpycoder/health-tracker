using System.Reflection;
using System.Text.Json;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Server.Sync;

/// <summary>
/// The bidirectional sync core, entity-agnostic. It discovers the mapped entity types
/// from the model at runtime and dispatches to a strongly-typed generic method per type
/// (reflection only picks the type argument — the LINQ itself is compile-checked).
///
/// Cursor model (v1): <c>UpdatedAt</c> (UTC), stamped server-side on every accepted write
/// (see <see cref="CloudDbContext"/>). "Changed since" = <c>UpdatedAt &gt; since</c>;
/// last-write-wins = server-arrival order. Fine for a single user; a dedicated server
/// row-version is a later hardening upgrade.
/// </summary>
public sealed class SyncEngine
{
    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly MethodInfo PullOneMi =
        typeof(SyncEngine).GetMethod(nameof(PullOneAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo PushOneMi =
        typeof(SyncEngine).GetMethod(nameof(PushOneAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo MaxOneMi =
        typeof(SyncEngine).GetMethod(nameof(MaxOneAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static IEnumerable<Type> EntityTypesOf(DbContext db) =>
        db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => typeof(EntityBase).IsAssignableFrom(t))
            .Distinct();

    // ---- Pull: everything newer than the client's cursor (tombstones included) ----

    public async Task<SyncPullResponse> PullAsync(CloudDbContext db, long sinceTicks)
    {
        var since = TicksToUtc(sinceTicks);
        var resp = new SyncPullResponse { Cursor = sinceTicks };
        foreach (var t in EntityTypesOf(db))
        {
            var task = (Task)PullOneMi.MakeGenericMethod(t).Invoke(this, new object[] { db, since, resp })!;
            await task;
        }
        return resp;
    }

    private async Task PullOneAsync<T>(CloudDbContext db, DateTime since, SyncPullResponse resp) where T : EntityBase
    {
        // IgnoreQueryFilters so tombstones (IsDeleted = true) are included — that's how a
        // delete reaches other clients.
        var rows = await db.Set<T>().IgnoreQueryFilters()
            .Where(e => e.UpdatedAt > since)
            .OrderBy(e => e.UpdatedAt)
            .ToListAsync();

        if (rows.Count == 0) return;

        resp.Changes[typeof(T).Name] = rows.Cast<object>().ToList();
        var maxTicks = rows.Max(r => r.UpdatedAt.Ticks);
        if (maxTicks > resp.Cursor) resp.Cursor = maxTicks;
    }

    // ---- Push: upsert locally-changed rows by Guid Id (idempotent) ----

    public async Task<int> PushAsync(CloudDbContext db, SyncPushRequest req)
    {
        var byName = EntityTypesOf(db).ToDictionary(t => t.Name);
        var applied = 0;
        foreach (var (name, rows) in req.Changes)
        {
            if (!byName.TryGetValue(name, out var t)) continue; // forward-compat: ignore unknown types
            var task = (Task<int>)PushOneMi.MakeGenericMethod(t).Invoke(this, new object[] { db, rows })!;
            applied += await task;
        }
        // Single SaveChanges: EF topologically orders inserts by FK, and CloudDbContext
        // stamps every accepted row with a fresh server UTC (the cursor).
        await db.SaveChangesAsync();
        return applied;
    }

    private async Task<int> PushOneAsync<T>(CloudDbContext db, List<JsonElement> rows) where T : EntityBase
    {
        var n = 0;
        foreach (var el in rows)
        {
            var incoming = el.Deserialize<T>(JsonOpts);
            if (incoming is null) continue;

            var existing = await db.Set<T>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == incoming.Id);

            if (existing is null)
                db.Add(incoming);                                   // new row (may be a tombstone)
            else
                db.Entry(existing).CurrentValues.SetValues(incoming); // last-write-wins overwrite

            n++;
        }
        return n;
    }

    // ---- Max cursor across all entity types (used to advance the client past its own push) ----

    public async Task<long> MaxCursorAsync(CloudDbContext db)
    {
        long max = 0;
        foreach (var t in EntityTypesOf(db))
        {
            var task = (Task<long>)MaxOneMi.MakeGenericMethod(t).Invoke(this, new object[] { db })!;
            var v = await task;
            if (v > max) max = v;
        }
        return max;
    }

    private async Task<long> MaxOneAsync<T>(CloudDbContext db) where T : EntityBase
    {
        var q = db.Set<T>().IgnoreQueryFilters();
        if (!await q.AnyAsync()) return 0;
        var max = await q.MaxAsync(e => e.UpdatedAt);
        return max.Ticks;
    }

    private static DateTime TicksToUtc(long ticks)
    {
        if (ticks <= 0) return DateTime.MinValue.ToUniversalTime();
        if (ticks > DateTime.MaxValue.Ticks) ticks = DateTime.MaxValue.Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}

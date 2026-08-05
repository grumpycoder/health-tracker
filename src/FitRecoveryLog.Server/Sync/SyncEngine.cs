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
        // delete reaches other clients. AsNoTracking is essential: without it, EF fixes up
        // navigation properties between the loaded entities, creating reference cycles that
        // the JSON serializer throws on mid-stream (truncating the response).
        var rows = await db.Set<T>().IgnoreQueryFilters().AsNoTracking()
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
        var incoming = rows.Select(el => el.Deserialize<T>(JsonOpts)).OfType<T>().ToList();
        if (incoming.Count == 0) return 0;

        // One query for all existing rows in this batch (EF Core 9 translates Contains via
        // OPENJSON on SQL Server, so no 2100-parameter limit) — avoids a round-trip per row.
        var ids = incoming.Select(x => x.Id).ToList();
        var existing = await db.Set<T>().IgnoreQueryFilters()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        foreach (var row in incoming)
        {
            if (existing.TryGetValue(row.Id, out var cur))
                db.Entry(cur).CurrentValues.SetValues(row); // last-write-wins overwrite
            else
                db.Add(row);                                // new row (may be a tombstone)
        }
        return incoming.Count;
    }

    private static DateTime TicksToUtc(long ticks)
    {
        if (ticks <= 0) return DateTime.MinValue.ToUniversalTime();
        if (ticks > DateTime.MaxValue.Ticks) ticks = DateTime.MaxValue.Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}

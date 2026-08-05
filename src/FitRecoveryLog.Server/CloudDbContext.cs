using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Server;

/// <summary>
/// The cloud (Azure SQL) context. Reuses the entire <see cref="AppDbContext"/> model
/// unchanged — the provider-specific bits in the base <c>OnModelCreating</c> already
/// switch to the SQL Server dialect. The only cloud-specific behavior is timestamp
/// stamping: on the server, <see cref="EntityBase.UpdatedAt"/> is the sync cursor, so
/// the server stamps it authoritatively on every write it accepts — including inserts
/// (the phone leaves inserts on their constructor stamp, but the server must not).
/// </summary>
public sealed class CloudDbContext : AppDbContext
{
    public CloudDbContext(DbContextOptions<CloudDbContext> options) : base(options) { }

    protected override void StampTimestamps()
    {
        // Server owns the clock: every accepted insert/update gets a fresh server UTC
        // stamp so the cursor is monotonic in server-arrival order. Deletes are still
        // routed through the base as tombstones.
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
        base.StampTimestamps();
    }
}

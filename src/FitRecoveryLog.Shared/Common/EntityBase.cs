namespace FitRecoveryLog.Data;

/// <summary>
/// Base for all records. GUID key so entries created offline on different devices
/// never collide. Timestamps are UTC and <see cref="UpdatedAt"/> is maintained by
/// the context, so future cloud sync can order/merge changes and last-write-wins.
/// Deletes are soft (tombstones) so a deletion on one device propagates to others.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Soft-delete tombstone. Rows are never physically removed in normal use
    /// (only a full restore/wipe clears them), so a delete can sync to other clients.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

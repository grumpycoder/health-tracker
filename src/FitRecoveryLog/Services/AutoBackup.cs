using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

/// <summary>
/// Local automatic backups: a timestamped JSON snapshot written to the app's
/// Documents on launch (at most once/day), keeping the most recent few.
/// NOTE: these live in the app container, so they survive updates and re-signs
/// but NOT an uninstall — off-device safety still needs a manual/iCloud export.
/// </summary>
public sealed class AutoBackup
{
    private const int KeepCount = 7;
    private const string LastSnapshotPref = "auto_backup_last";
    private const string LastExportPref = "auto_backup_last_export";
    private const string ReminderStampPref = "auto_backup_reminder_stamp";

    /// <summary>When the user last exported an off-device backup (Export JSON), if ever.</summary>
    public DateTime? LastExport =>
        DateTime.TryParse(Preferences.Default.Get(LastExportPref, ""), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;

    public void MarkExported() =>
        Preferences.Default.Set(LastExportPref, DateTime.Now.ToString("O"));

    /// <summary>Schedule the weekly "export a backup" nudge ONCE per export cycle — a
    /// week after the last export (or first launch). Re-runs only when the last-export
    /// timestamp changes, so it isn't rescheduled/re-fired on every app open.</summary>
    public async Task EnsureBackupReminderAsync()
    {
        var stamp = LastExport?.ToString("O") ?? "never";
        if (Preferences.Default.Get(ReminderStampPref, "") == stamp) return;
        var due = (LastExport ?? DateTime.Now).AddDays(7);
        if (due <= DateTime.Now) due = DateTime.Now.AddDays(1); // overdue → tomorrow, not now
        try { await ReminderNotifier.ScheduleBackupReminderAsync(due); } catch { }
        Preferences.Default.Set(ReminderStampPref, stamp);
    }

    private readonly IDbContextFactory<AppDbContext> _factory;
    public AutoBackup(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    private static string Dir
    {
        get
        {
            var d = Path.Combine(FileSystem.AppDataDirectory, "backups");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Write today's snapshot if one hasn't been made today; prune old ones.</summary>
    public async Task SnapshotIfDueAsync()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        // Skip only if today's snapshot is already done AND at least one file exists.
        if (Preferences.Default.Get(LastSnapshotPref, "") == today && Latest() is not null) return;
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var json = await BackupRestore.BuildJsonAsync(db);
            var path = Path.Combine(Dir, $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, json);
            Preferences.Default.Set(LastSnapshotPref, today);
            Prune();
        }
        catch { /* backup is best-effort; never block startup */ }
    }

    private static void Prune()
    {
        var files = new DirectoryInfo(Dir).GetFiles("backup-*.json")
            .OrderByDescending(f => f.Name).Skip(KeepCount);
        foreach (var f in files) { try { f.Delete(); } catch { } }
    }

    /// <summary>Newest local snapshot (path + when), or null if none exist.</summary>
    public (string Path, DateTime When)? Latest()
    {
        var newest = new DirectoryInfo(Dir).GetFiles("backup-*.json")
            .OrderByDescending(f => f.Name).FirstOrDefault();
        return newest is null ? null : (newest.FullName, newest.LastWriteTime);
    }
}

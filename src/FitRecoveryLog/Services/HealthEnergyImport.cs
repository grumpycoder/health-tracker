using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Services;

/// <summary>Copies Apple Health resting + active energy onto <see cref="DailyLog"/> rows, so the
/// energy balance (phone and web, via sync) uses measured burn: recently completed days (re-read
/// because the Watch can sync late) plus today's activity so far, which feeds today's projection.
/// Only writes when a value changed, so it doesn't churn sync. Runs one at a time — it's
/// triggered from both app resume and the Home page.</summary>
public sealed class HealthEnergyImport
{
    private const int DaysBack = 14;
    private readonly IHealthService _health;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HealthEnergyImport(IHealthService health, IDbContextFactory<AppDbContext> dbFactory)
    {
        _health = health;
        _dbFactory = dbFactory;
    }

    public async Task RunAsync()
    {
        if (!_health.IsAvailable) return;
        await _gate.WaitAsync();
        try { await ImportAsync(); }
        finally { _gate.Release(); }
    }

    private async Task ImportAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var logs = await db.DailyLogs.Where(d => d.Date >= today.AddDays(-DaysBack) && d.Date <= today)
            .ToDictionaryAsync(d => d.Date);
        for (var date = today.AddDays(-DaysBack); date <= today; date = date.AddDays(1))
        {
            var (resting, active) = await _health.ReadEnergyAsync(date);
            // Completed days need both for a real total. Today stores activity so far only: its
            // resting is partial, and leaving it null keeps the row from passing as a complete day
            // (e.g. on the web the next morning) until this re-reads it once the day is over.
            if (active is null || (date < today && resting is not > 0)) continue;
            if (date == today) resting = null;
            if (!logs.TryGetValue(date, out var log))
                db.DailyLogs.Add(log = new DailyLog { Date = date });
            else if (log.RestingEnergyKcal == resting && log.ActiveEnergyKcal == active)
                continue;
            log.RestingEnergyKcal = resting;
            log.ActiveEnergyKcal = active;
        }
        await db.SaveChangesAsync();
    }
}

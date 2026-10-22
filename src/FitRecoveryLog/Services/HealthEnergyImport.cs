using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Services;

/// <summary>Copies Apple Health resting + active energy for recently completed days onto their
/// <see cref="DailyLog"/> rows, so the energy balance (phone and web, via sync) uses measured
/// burn. Re-reads a short window because the Watch can sync a day's samples late; only writes
/// when a value changed, so it doesn't churn sync.</summary>
public sealed class HealthEnergyImport
{
    private const int DaysBack = 14;
    private readonly IHealthService _health;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HealthEnergyImport(IHealthService health, IDbContextFactory<AppDbContext> dbFactory)
    {
        _health = health;
        _dbFactory = dbFactory;
    }

    public async Task RunAsync()
    {
        if (!_health.IsAvailable) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var logs = await db.DailyLogs.Where(d => d.Date >= today.AddDays(-DaysBack) && d.Date < today)
            .ToDictionaryAsync(d => d.Date);
        for (var date = today.AddDays(-DaysBack); date < today; date = date.AddDays(1))
        {
            var (resting, active) = await _health.ReadEnergyAsync(date);
            if (resting is not > 0 || active is null) continue;   // need both for a real total
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

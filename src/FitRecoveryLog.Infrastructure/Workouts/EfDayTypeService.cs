using FitRecoveryLog.Application.Workouts;
using Microsoft.EntityFrameworkCore;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Infrastructure.Workouts;

/// <summary>EF implementation of the day-type port: marks a date as a workout day by
/// upserting its <see cref="Persistence.DailyLog"/>. Invoked by the WorkoutCompleted handler
/// when a workout is finished.</summary>
public sealed class EfDayTypeService : IDayTypeService
{
    private readonly IDbContextFactory<Persistence.AppDbContext> _factory;

    public EfDayTypeService(IDbContextFactory<Persistence.AppDbContext> factory) => _factory = factory;

    public async Task MarkWorkoutDayAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var log = await db.DailyLogs.FirstOrDefaultAsync(d => d.Date == date, ct);
        if (log is null)
        {
            db.DailyLogs.Add(new Persistence.DailyLog { Date = date, DayType = Persistence.DayType.Workout });
        }
        else if (log.DayType == Persistence.DayType.Unset)
        {
            log.DayType = Persistence.DayType.Workout;
        }
        else
        {
            return; // a deliberately-chosen day type is left alone
        }
        await db.SaveChangesAsync(ct);
    }
}

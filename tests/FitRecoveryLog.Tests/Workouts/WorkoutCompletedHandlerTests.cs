using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Workouts.Events;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

[TestFixture]
public class WorkoutCompletedHandlerTests
{
    [Test]
    public async Task Handle_MarksTheWorkoutDay()
    {
        var days = new FakeDayTypeService();
        var handler = new WorkoutCompletedHandler(days);
        var date = new DateOnly(2026, 8, 5);

        await handler.HandleAsync(new WorkoutCompleted(Guid.NewGuid(), date));

        Assert.That(days.Marked, Does.Contain(date));
    }

    private sealed class FakeDayTypeService : IDayTypeService
    {
        public readonly List<DateOnly> Marked = new();
        public Task MarkWorkoutDayAsync(DateOnly date, CancellationToken ct = default) { Marked.Add(date); return Task.CompletedTask; }
    }
}

using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Workouts;
using FitRecoveryLog.Domain.Workouts.Events;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

[TestFixture]
public class EventDispatchingWorkoutRepositoryTests
{
    private static readonly DateOnly Day = new(2026, 8, 5);

    [Test]
    public async Task Save_DispatchesRaisedEvents_ThenClears()
    {
        var inner = new FakeInner();
        var dispatcher = new FakeDispatcher();
        var repo = new EventDispatchingWorkoutRepository(inner, dispatcher);

        var session = WorkoutSession.Create(Day);
        session.Finish(new DateTime(2026, 8, 5, 7, 0, 0, DateTimeKind.Utc)); // raises WorkoutCompleted

        await repo.SaveAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(inner.Saved, Is.True, "the inner repo still persists");
            Assert.That(dispatcher.Dispatched.OfType<WorkoutCompleted>().Any(), Is.True, "raised events are dispatched on save");
            Assert.That(session.DomainEvents, Is.Empty, "events are cleared after dispatch");
        });
    }

    [Test]
    public async Task Save_NoEvents_DoesNotDispatch()
    {
        var inner = new FakeInner();
        var dispatcher = new FakeDispatcher();
        var repo = new EventDispatchingWorkoutRepository(inner, dispatcher);

        await repo.SaveAsync(WorkoutSession.Create(Day)); // no Finish -> no events

        Assert.Multiple(() =>
        {
            Assert.That(inner.Saved, Is.True);
            Assert.That(dispatcher.Dispatched, Is.Empty);
        });
    }

    private sealed class FakeInner : IWorkoutRepository
    {
        public bool Saved;
        public Task<WorkoutSession?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WorkoutSession?>(null);
        public Task<IReadOnlyList<WorkoutSession>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkoutSession>>(new List<WorkoutSession>());
        public Task SaveAsync(WorkoutSession session, CancellationToken ct = default) { Saved = true; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeDispatcher : IDomainEventDispatcher
    {
        public readonly List<IDomainEvent> Dispatched = new();
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            Dispatched.AddRange(events);
            return Task.CompletedTask;
        }
    }
}

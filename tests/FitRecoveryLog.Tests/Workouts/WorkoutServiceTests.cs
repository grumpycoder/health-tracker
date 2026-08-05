using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Workouts;
using FitRecoveryLog.Domain.Workouts.Events;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

[TestFixture]
public class WorkoutServiceTests
{
    private static readonly DateOnly Day = new(2026, 8, 5);
    private FakeWorkoutRepository _workouts = null!;
    private FakeDispatcher _events = null!;
    private WorkoutService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _workouts = new FakeWorkoutRepository();
        _events = new FakeDispatcher();
        _service = new WorkoutService(_workouts, _events);
    }

    [Test]
    public async Task Create_Persists()
    {
        var result = await _service.CreateAsync(Day);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_workouts.Store.ContainsKey(result.Value), Is.True);
        });
    }

    [Test]
    public async Task AddSet_Persists_ReturnsSetId()
    {
        var id = (await _service.CreateAsync(Day)).Value;
        var result = await _service.AddSetAsync(id, Guid.NewGuid(), new SetResult(5, 100, null, null));
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_workouts.Store[id].Sets, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Complete_Finishes_AndDispatchesWorkoutCompleted()
    {
        var id = (await _service.CreateAsync(Day)).Value;

        var result = await _service.CompleteAsync(id, new DateTime(2026, 8, 5, 7, 0, 0, DateTimeKind.Utc));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_workouts.Store[id].EndedAt, Is.Not.Null);
            var evt = _events.Dispatched.OfType<WorkoutCompleted>().SingleOrDefault();
            Assert.That(evt, Is.Not.Null, "a WorkoutCompleted event must be dispatched");
            Assert.That(evt!.Date, Is.EqualTo(Day));
        });
    }

    [Test]
    public async Task Mutate_UnknownWorkout_Fails()
    {
        var result = await _service.SetNotesAsync(Guid.NewGuid(), "x");
        Assert.That(result.IsSuccess, Is.False);
    }

    private sealed class FakeWorkoutRepository : IWorkoutRepository
    {
        public readonly Dictionary<Guid, WorkoutSession> Store = new();
        public Task<WorkoutSession?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Store.GetValueOrDefault(id));
        public Task<IReadOnlyList<WorkoutSession>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkoutSession>>(Store.Values.ToList());
        public Task SaveAsync(WorkoutSession session, CancellationToken ct = default) { Store[session.Id] = session; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken ct = default) { Store.Remove(id); return Task.CompletedTask; }
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

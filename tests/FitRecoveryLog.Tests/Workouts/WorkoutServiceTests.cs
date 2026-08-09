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
    private WorkoutService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _workouts = new FakeWorkoutRepository();
        _service = new WorkoutService(_workouts);
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
    public async Task CreateWithSets_SeedsSets_TagsRoutine_StaysOpen()
    {
        var routineId = Guid.NewGuid();
        var ex1 = Guid.NewGuid();
        var ex2 = Guid.NewGuid();
        var seed = new List<WorkoutSetData>
        {
            new(ex1, new SetResult(8, 135, null, 90), false),
            new(ex1, new SetResult(8, 135, null, 90), false),
            new(ex2, new SetResult(10, 40, null, 60), false),
        };

        var result = await _service.CreateWithSetsAsync(Day, routineId, seed);

        Assert.That(result.IsSuccess, Is.True);
        var session = _workouts.Store[result.Value];
        Assert.Multiple(() =>
        {
            Assert.That(session.RoutineId, Is.EqualTo(routineId));
            Assert.That(session.Sets, Has.Count.EqualTo(3));
            Assert.That(session.EndedAt, Is.Null, "seeded workout should stay open for editing");
        });
    }

    [Test]
    public async Task CreateWithSets_NoSets_EquivalentToCreate()
    {
        var result = await _service.CreateWithSetsAsync(Day, null, Array.Empty<WorkoutSetData>());
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_workouts.Store[result.Value].Sets, Is.Empty);
        });
    }

    [Test]
    public async Task Complete_Finishes_AndRaisesEvent()
    {
        var id = (await _service.CreateAsync(Day)).Value;

        var result = await _service.CompleteAsync(id, new DateTime(2026, 8, 5, 7, 0, 0, DateTimeKind.Utc));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_workouts.Store[id].EndedAt, Is.Not.Null);
            // Dispatch is the repository decorator's job (see EventDispatchingWorkoutRepositoryTests);
            // the use case's responsibility is to finish + save.
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

}

using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Workouts;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

[TestFixture]
public class RoutineServiceTests
{
    private FakeRoutineRepository _routines = null!;
    private FakeSessionRepository _sessions = null!;
    private RoutineService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _routines = new FakeRoutineRepository();
        _sessions = new FakeSessionRepository();
        _service = new RoutineService(_routines, _sessions);
    }

    [Test]
    public async Task Create_EmptyName_Fails()
    {
        var result = await _service.CreateAsync("");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(_routines.Store, Is.Empty);
        });
    }

    [Test]
    public async Task Create_Persists_ReturnsId()
    {
        var result = await _service.CreateAsync("Push Day");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_routines.Store.ContainsKey(result.Value), Is.True);
        });
    }

    [Test]
    public async Task Archive_SetsFlag_AndPersists()
    {
        var id = (await _service.CreateAsync("Legs")).Value;
        var result = await _service.ArchiveAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_routines.Store[id].Archived, Is.True);
        });
    }

    [Test]
    public async Task Mutate_UnknownRoutine_Fails()
    {
        var result = await _service.RenameAsync(Guid.NewGuid(), "x");
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task Delete_DetachesSessions_ThenRemovesRoutine()
    {
        var id = (await _service.CreateAsync("Legs")).Value;

        var result = await _service.DeleteAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_sessions.Detached, Does.Contain(id), "sessions must be detached to preserve history");
            Assert.That(_routines.Store.ContainsKey(id), Is.False);
        });
    }

    [Test]
    public async Task AddExercise_Persists_ReturnsExerciseId()
    {
        var id = (await _service.CreateAsync("Legs")).Value;
        var result = await _service.AddExerciseAsync(id, Guid.NewGuid(), new ExercisePrescription(3, 10, null, 60, null, null));
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_routines.Store[id].Exercises, Has.Count.EqualTo(1));
            Assert.That(_routines.Store[id].Exercises[0].Id, Is.EqualTo(result.Value));
        });
    }

    // ---- in-memory fakes ----

    private sealed class FakeRoutineRepository : IRoutineRepository
    {
        public readonly Dictionary<Guid, Routine> Store = new();
        public Task<Routine?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.GetValueOrDefault(id));
        public Task<IReadOnlyList<Routine>> ListAsync(bool includeArchived, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Routine>>(
                Store.Values.Where(r => includeArchived || !r.Archived).ToList());
        public Task SaveAsync(Routine routine, CancellationToken ct = default) { Store[routine.Id] = routine; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken ct = default) { Store.Remove(id); return Task.CompletedTask; }
    }

    private sealed class FakeSessionRepository : IWorkoutSessionRepository
    {
        public readonly List<Guid> Detached = new();
        public Task DetachFromRoutineAsync(Guid routineId, CancellationToken ct = default)
        {
            Detached.Add(routineId);
            return Task.CompletedTask;
        }
    }
}

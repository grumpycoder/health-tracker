using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Workouts;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

[TestFixture]
public class WorkoutServiceTests
{
    private static readonly DateOnly Day = new(2026, 8, 5);
    private FakeWorkoutRepository _workouts = null!;
    private FakeDayTypeService _days = null!;
    private WorkoutService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _workouts = new FakeWorkoutRepository();
        _days = new FakeDayTypeService();
        _service = new WorkoutService(_workouts, _days);
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
    public async Task Complete_Finishes_AndMarksWorkoutDay()
    {
        var id = (await _service.CreateAsync(Day)).Value;

        var result = await _service.CompleteAsync(id, new DateTime(2026, 8, 5, 7, 0, 0, DateTimeKind.Utc));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_workouts.Store[id].EndedAt, Is.Not.Null);
            Assert.That(_days.Marked, Does.Contain(Day), "the workout's day must be marked as a workout day");
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

    private sealed class FakeDayTypeService : IDayTypeService
    {
        public readonly List<DateOnly> Marked = new();
        public Task MarkWorkoutDayAsync(DateOnly date, CancellationToken ct = default) { Marked.Add(date); return Task.CompletedTask; }
    }
}

using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Workouts;
using FitRecoveryLog.Infrastructure.Workouts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

/// <summary>
/// Integration tests for the real EF repository against an in-memory SQLite database —
/// exercises the actual domain↔persistence mapping, soft-delete, and query filters.
/// </summary>
[TestFixture]
public class EfRoutineRepositoryTests
{
    private SqliteConnection _connection = null!;
    private TestDbContextFactory _factory = null!;
    private EfRoutineRepository _repo = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        using (var db = _factory.CreateDbContext()) db.Database.EnsureCreated();
        _repo = new EfRoutineRepository(_factory);
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public async Task Save_ThenGet_RoundTrips()
    {
        var routine = Routine.Create("Push Day", "chest + shoulders");
        routine.AddExercise(await SeedExerciseAsync("Bench"), new ExercisePrescription(3, 10, null, 60, 45, "each side"));
        routine.AddExercise(await SeedExerciseAsync("Press"), new ExercisePrescription(4, 8, null, 90, null, null));

        await _repo.SaveAsync(routine);
        var loaded = await _repo.GetAsync(routine.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Name, Is.EqualTo("Push Day"));
            Assert.That(loaded.Notes, Is.EqualTo("chest + shoulders"));
            Assert.That(loaded.Exercises, Has.Count.EqualTo(2));
            Assert.That(loaded.Exercises.Select(e => e.Order), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(loaded.Exercises[0].Prescription.TargetNote, Is.EqualTo("each side"));
        });
    }

    [Test]
    public async Task Save_ReconcilesRemovedExercise()
    {
        var routine = Routine.Create("Legs");
        var first = routine.AddExercise(await SeedExerciseAsync("Squat"), ExercisePrescription.None);
        routine.AddExercise(await SeedExerciseAsync("Lunge"), ExercisePrescription.None);
        await _repo.SaveAsync(routine);

        routine.RemoveExercise(first);
        await _repo.SaveAsync(routine);

        var loaded = await _repo.GetAsync(routine.Id);
        Assert.That(loaded!.Exercises, Has.Count.EqualTo(1));
        Assert.That(loaded.Exercises.Select(e => e.Order), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task List_ExcludesArchived_WhenAsked()
    {
        var active = Routine.Create("Active");
        var archived = Routine.Create("Archived");
        archived.Archive();
        await _repo.SaveAsync(active);
        await _repo.SaveAsync(archived);

        var activeOnly = await _repo.ListAsync(includeArchived: false);
        var all = await _repo.ListAsync(includeArchived: true);

        Assert.Multiple(() =>
        {
            Assert.That(activeOnly.Select(r => r.Name), Is.EquivalentTo(new[] { "Active" }));
            Assert.That(all, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Remove_SoftDeletes_GetReturnsNull()
    {
        var routine = Routine.Create("Temp");
        await _repo.SaveAsync(routine);

        await _repo.RemoveAsync(routine.Id);

        Assert.That(await _repo.GetAsync(routine.Id), Is.Null);
    }

    [Test]
    public async Task DetachFromRoutine_NullsSessionRoutineId()
    {
        var routine = Routine.Create("Legs");
        await _repo.SaveAsync(routine);
        using (var db = _factory.CreateDbContext())
        {
            db.WorkoutSessions.Add(new WorkoutSession { Date = new DateOnly(2026, 8, 1), RoutineId = routine.Id });
            await db.SaveChangesAsync();
        }

        await new EfWorkoutSessionRepository(_factory).DetachFromRoutineAsync(routine.Id);

        using (var db = _factory.CreateDbContext())
            Assert.That(db.WorkoutSessions.Single().RoutineId, Is.Null);
    }

    private async Task<Guid> SeedExerciseAsync(string name)
    {
        using var db = _factory.CreateDbContext();
        var def = new ExerciseDefinition { Name = name };
        db.ExerciseDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.Id;
    }

    // Minimal IDbContextFactory over a shared in-memory SQLite connection.
    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestDbContextFactory(SqliteConnection connection) => _connection = connection;
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
    }
}

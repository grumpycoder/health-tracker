using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Workouts;
using FitRecoveryLog.Domain.Workouts.Events;
using FitRecoveryLog.Infrastructure.Workouts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Tests.Workouts;

/// <summary>Integration tests for the EF workout repository + day-type service against
/// in-memory SQLite — the session/sets/feedback mapping and child reconciliation.</summary>
[TestFixture]
public class EfWorkoutRepositoryTests
{
    private SqliteConnection _connection = null!;
    private TestDbContextFactory _factory = null!;
    private EfWorkoutRepository _repo = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        using (var db = _factory.CreateDbContext()) db.Database.EnsureCreated();
        _repo = new EfWorkoutRepository(_factory);
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public async Task Save_ThenGet_RoundTripsSetsAndFeedback()
    {
        var squat = await SeedExerciseAsync("Squat");
        var session = WorkoutSession.Create(new DateOnly(2026, 8, 5));
        session.AddSet(squat, new SetResult(10, 135, null, 60), completed: true);
        session.AddSet(squat, new SetResult(8, 145, null, 90), completed: true);
        session.SetFeedback(squat, Difficulty.Hard, pain: false, breathing: false, form: true, "felt heavy");
        await _repo.SaveAsync(session);

        var loaded = await _repo.GetAsync(session.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Sets, Has.Count.EqualTo(2));
            Assert.That(loaded.Sets.Select(s => s.SetNumber), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(loaded.Sets[0].Result.Weight, Is.EqualTo(135));
            Assert.That(loaded.Feedback, Has.Count.EqualTo(1));
            Assert.That(loaded.Feedback[0].Difficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(loaded.Feedback[0].FormIssues, Is.True);
        });
    }

    [Test]
    public async Task AddSet_ToAlreadyPersistedSession_Inserts()
    {
        // Regression (same footgun as routines): create the session, then add a set in a second
        // save. The new child must INSERT, not UPDATE a non-existent row.
        var squat = await SeedExerciseAsync("Squat");
        var session = WorkoutSession.Create(new DateOnly(2026, 8, 5));
        await _repo.SaveAsync(session);

        session.AddSet(squat, new SetResult(5, 225, null, 120), completed: true);
        await _repo.SaveAsync(session);

        var loaded = await _repo.GetAsync(session.Id);
        Assert.That(loaded!.Sets, Has.Count.EqualTo(1));
        Assert.That(loaded.Sets[0].Result.Weight, Is.EqualTo(225));
    }

    [Test]
    public async Task RemoveSet_TombstonesChild()
    {
        var squat = await SeedExerciseAsync("Squat");
        var session = WorkoutSession.Create(new DateOnly(2026, 8, 5));
        var s1 = session.AddSet(squat, new SetResult(10, 100, null, 60));
        session.AddSet(squat, new SetResult(10, 100, null, 60));
        await _repo.SaveAsync(session);

        session.RemoveSet(s1);
        await _repo.SaveAsync(session);

        var loaded = await _repo.GetAsync(session.Id);
        Assert.That(loaded!.Sets, Has.Count.EqualTo(1));
        Assert.That(loaded.Sets.Select(s => s.SetNumber), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task DayTypeService_MarksWorkoutDay()
    {
        var date = new DateOnly(2026, 8, 5);
        await new EfDayTypeService(_factory).MarkWorkoutDayAsync(date);

        using var db = _factory.CreateDbContext();
        var log = db.DailyLogs.Single(d => d.Date == date);
        Assert.That(log.DayType, Is.EqualTo(Persistence.DayType.Workout));
    }

    [Test]
    public async Task LogCompleted_MarksWorkoutDay_ThroughTheDomainEvent()
    {
        // End-to-end phone pipeline: WorkoutService -> EventDispatchingWorkoutRepository (decorator)
        // -> DomainEventDispatcher -> WorkoutCompletedHandler -> EfDayTypeService.
        var squat = await SeedExerciseAsync("Squat");
        var handler = new WorkoutCompletedHandler(new EfDayTypeService(_factory));
        var dispatcher = new DomainEventDispatcher(
            new StubProvider(new IDomainEventHandler<WorkoutCompleted>[] { handler }));
        var svc = new WorkoutService(new EventDispatchingWorkoutRepository(new EfWorkoutRepository(_factory), dispatcher));

        var date = new DateOnly(2026, 8, 5);
        var data = new CompletedWorkoutData(date, null, DateTime.Now.AddMinutes(-30), DateTime.Now, 1800, "leg day",
            new[] { new WorkoutSetData(squat, new SetResult(10, 135, null, 60), true) },
            Array.Empty<WorkoutFeedbackData>());

        var result = await svc.LogCompletedAsync(data);

        Assert.That(result.IsSuccess, Is.True, result.Error);
        using var db = _factory.CreateDbContext();
        Assert.Multiple(() =>
        {
            Assert.That(db.DailyLogs.SingleOrDefault(d => d.Date == date)?.DayType, Is.EqualTo(Persistence.DayType.Workout));
            Assert.That(db.WorkoutSessions.Include(s => s.Sets).Single().Sets, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task DayTypeService_LeavesADeliberateDayTypeAlone()
    {
        var date = new DateOnly(2026, 8, 5);
        using (var db = _factory.CreateDbContext())
        {
            db.DailyLogs.Add(new Persistence.DailyLog { Date = date, DayType = Persistence.DayType.HighWorkload });
            await db.SaveChangesAsync();
        }

        await new EfDayTypeService(_factory).MarkWorkoutDayAsync(date);

        using var verify = _factory.CreateDbContext();
        Assert.That(verify.DailyLogs.Single(d => d.Date == date).DayType,
            Is.EqualTo(Persistence.DayType.HighWorkload), "a deliberately-chosen day type must be preserved");
    }

    /// <summary>Minimal IServiceProvider that resolves the WorkoutCompleted handler list —
    /// avoids pulling a DI container into the test.</summary>
    private sealed class StubProvider : IServiceProvider
    {
        private readonly IEnumerable<IDomainEventHandler<WorkoutCompleted>> _handlers;
        public StubProvider(IEnumerable<IDomainEventHandler<WorkoutCompleted>> handlers) => _handlers = handlers;
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IEnumerable<IDomainEventHandler<WorkoutCompleted>>) ? _handlers : null;
    }

    private async Task<Guid> SeedExerciseAsync(string name)
    {
        using var db = _factory.CreateDbContext();
        var def = new Persistence.ExerciseDefinition { Name = name };
        db.ExerciseDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.Id;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<Persistence.AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestDbContextFactory(SqliteConnection connection) => _connection = connection;
        public Persistence.AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<Persistence.AppDbContext>().UseSqlite(_connection).Options);
    }
}

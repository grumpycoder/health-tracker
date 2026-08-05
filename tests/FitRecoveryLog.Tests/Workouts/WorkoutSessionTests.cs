using FitRecoveryLog.Domain.Workouts;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

[TestFixture]
public class WorkoutSessionTests
{
    private static readonly DateOnly Today = new(2026, 8, 5);
    private static SetResult Reps(int n, double? w = null) => new(n, w, null, null);

    [Test]
    public void AddSet_NumbersPerExercise()
    {
        var s = WorkoutSession.Create(Today);
        var squat = Guid.NewGuid();
        var bench = Guid.NewGuid();
        s.AddSet(squat, Reps(5));
        s.AddSet(squat, Reps(5));
        s.AddSet(bench, Reps(8));

        Assert.Multiple(() =>
        {
            Assert.That(s.Sets.Where(x => x.ExerciseDefinitionId == squat).Select(x => x.SetNumber), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(s.Sets.Where(x => x.ExerciseDefinitionId == bench).Select(x => x.SetNumber), Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void RemoveSet_RenumbersThatExerciseOnly()
    {
        var s = WorkoutSession.Create(Today);
        var squat = Guid.NewGuid();
        var first = s.AddSet(squat, Reps(5));
        s.AddSet(squat, Reps(5));
        s.AddSet(squat, Reps(5));

        s.RemoveSet(first);

        Assert.That(s.Sets.Where(x => x.ExerciseDefinitionId == squat).Select(x => x.SetNumber), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void UpdateSet_ChangesResultAndCompleted()
    {
        var s = WorkoutSession.Create(Today);
        var id = s.AddSet(Guid.NewGuid(), Reps(5, 100));
        s.UpdateSet(id, Reps(6, 105), completed: true);
        var set = s.Sets.Single();
        Assert.Multiple(() =>
        {
            Assert.That(set.Result.Reps, Is.EqualTo(6));
            Assert.That(set.Result.Weight, Is.EqualTo(105));
            Assert.That(set.Completed, Is.True);
        });
    }

    [Test]
    public void SetFeedback_UpsertsOnePerExercise()
    {
        var s = WorkoutSession.Create(Today);
        var ex = Guid.NewGuid();
        s.SetFeedback(ex, Difficulty.Hard, pain: false, breathing: false, form: false, comment: "tough");
        s.SetFeedback(ex, Difficulty.Moderate, pain: true, breathing: false, form: false, comment: null);

        Assert.Multiple(() =>
        {
            Assert.That(s.Feedback, Has.Count.EqualTo(1));
            Assert.That(s.Feedback[0].Difficulty, Is.EqualTo(Difficulty.Moderate));
            Assert.That(s.Feedback[0].PainOrDiscomfort, Is.True);
        });
    }

    [Test]
    public void Finish_DerivesDurationFromStart()
    {
        var s = WorkoutSession.Create(Today);
        var start = new DateTime(2026, 8, 5, 6, 0, 0, DateTimeKind.Utc);
        s.Start(start);
        s.Finish(start.AddMinutes(45));
        Assert.That(s.TotalSeconds, Is.EqualTo(45 * 60));
    }

    [Test]
    public void Finish_RaisesWorkoutCompleted()
    {
        var s = WorkoutSession.Create(Today);
        s.Finish(new DateTime(2026, 8, 5, 7, 0, 0, DateTimeKind.Utc));
        var evt = s.DomainEvents.OfType<FitRecoveryLog.Domain.Workouts.Events.WorkoutCompleted>().SingleOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(evt, Is.Not.Null);
            Assert.That(evt!.SessionId, Is.EqualTo(s.Id));
            Assert.That(evt.Date, Is.EqualTo(Today));
        });
    }

    [Test]
    public void Finish_BeforeStart_Throws()
    {
        var s = WorkoutSession.Create(Today);
        var start = new DateTime(2026, 8, 5, 6, 0, 0, DateTimeKind.Utc);
        s.Start(start);
        Assert.Throws<ArgumentException>(() => s.Finish(start.AddMinutes(-5)));
    }

    [Test]
    public void RemoveSet_Unknown_Throws()
    {
        var s = WorkoutSession.Create(Today);
        Assert.Throws<InvalidOperationException>(() => s.RemoveSet(Guid.NewGuid()));
    }

    [Test]
    public void Rehydrate_RenumbersStoredSetsPerExercise()
    {
        var ex = Guid.NewGuid();
        var sets = new[]
        {
            WorkoutSet.Rehydrate(Guid.NewGuid(), ex, 7, SetResult.None, false),
            WorkoutSet.Rehydrate(Guid.NewGuid(), ex, 3, SetResult.None, false),
        };
        var s = WorkoutSession.Rehydrate(Guid.NewGuid(), Today, null, null, null, null, null,
            sets, Array.Empty<WorkoutFeedback>());
        Assert.That(s.Sets.Select(x => x.SetNumber), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void SetResult_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SetResult(-1, null, null, null));
    }
}

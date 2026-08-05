using FitRecoveryLog.Domain.Workouts;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Workouts;

[TestFixture]
public class RoutineTests
{
    private static ExercisePrescription Rx(int sets = 3, int reps = 10) =>
        new(sets, reps, null, 60, null, null);

    [Test]
    public void Create_RequiresName()
    {
        Assert.Throws<ArgumentException>(() => Routine.Create("  "));
    }

    [Test]
    public void Create_TrimsNameAndStartsEmptyAndActive()
    {
        var r = Routine.Create("  Push Day  ");
        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("Push Day"));
            Assert.That(r.Archived, Is.False);
            Assert.That(r.Exercises, Is.Empty);
        });
    }

    [Test]
    public void AddExercise_AssignsContiguousOrder()
    {
        var r = Routine.Create("Legs");
        r.AddExercise(Guid.NewGuid(), Rx());
        r.AddExercise(Guid.NewGuid(), Rx());
        r.AddExercise(Guid.NewGuid(), Rx());
        Assert.That(r.Exercises.Select(e => e.Order), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void RemoveExercise_RenormalizesOrder()
    {
        var r = Routine.Create("Legs");
        r.AddExercise(Guid.NewGuid(), Rx());
        var middle = r.AddExercise(Guid.NewGuid(), Rx());
        r.AddExercise(Guid.NewGuid(), Rx());

        r.RemoveExercise(middle);

        Assert.That(r.Exercises.Select(e => e.Order), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void MoveExercise_ReordersAndClosesGap()
    {
        var r = Routine.Create("Legs");
        var first = r.AddExercise(Guid.NewGuid(), Rx());
        r.AddExercise(Guid.NewGuid(), Rx());
        r.AddExercise(Guid.NewGuid(), Rx());

        r.MoveExercise(first, 3); // move first to last

        Assert.Multiple(() =>
        {
            Assert.That(r.Exercises.Select(e => e.Order), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(r.Exercises.Last().Id, Is.EqualTo(first));
        });
    }

    [Test]
    public void UpdateExercise_ChangesPrescription()
    {
        var r = Routine.Create("Legs");
        var id = r.AddExercise(Guid.NewGuid(), Rx(3, 10));
        r.UpdateExercise(id, Rx(5, 5));
        var ex = r.Exercises.Single(e => e.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(ex.Prescription.TargetSets, Is.EqualTo(5));
            Assert.That(ex.Prescription.TargetReps, Is.EqualTo(5));
        });
    }

    [Test]
    public void RemoveOrUpdate_UnknownExercise_Throws()
    {
        var r = Routine.Create("Legs");
        Assert.Throws<InvalidOperationException>(() => r.RemoveExercise(Guid.NewGuid()));
    }

    [Test]
    public void Rename_Empty_Throws()
    {
        var r = Routine.Create("Legs");
        Assert.Throws<ArgumentException>(() => r.Rename(""));
    }

    [Test]
    public void ArchiveAndRestore_TogglesState()
    {
        var r = Routine.Create("Legs");
        r.Archive();
        Assert.That(r.Archived, Is.True);
        r.Restore();
        Assert.That(r.Archived, Is.False);
    }

    [Test]
    public void Rehydrate_NormalizesStoredOrderGaps()
    {
        var e1 = RoutineExercise.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), 5, ExercisePrescription.None);
        var e2 = RoutineExercise.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), 20, ExercisePrescription.None);
        var r = Routine.Rehydrate(Guid.NewGuid(), "Legs", null, false, new[] { e2, e1 });
        Assert.That(r.Exercises.Select(e => e.Order), Is.EqualTo(new[] { 1, 2 }));
    }
}

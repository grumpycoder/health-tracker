using FitRecoveryLog.Domain.Body;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Recovery;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Recovery;

[TestFixture]
public class RecoveryDomainTests
{
    private static readonly DateOnly Day = new(2026, 8, 5);

    [TestCase(0, null)]
    [TestCase(11, null)]
    [TestCase(7, 7)]
    public void Recovery_RatingsClampToOneToTen(int input, int? expected)
    {
        var r = RecoveryLog.Create(Day);
        r.SetRecoveryRating(input);
        r.SetFatigueRating(input);
        Assert.Multiple(() =>
        {
            Assert.That(r.RecoveryRating, Is.EqualTo(expected));
            Assert.That(r.FatigueRating, Is.EqualTo(expected));
        });
    }

    [Test]
    public void Recovery_SorenessUsesTagsValueObject()
    {
        var r = RecoveryLog.Create(Day);
        r.SetSoreness(Tags.FromCsv("Quads, Hamstrings, quads"), SorenessSeverity.Moderate);
        Assert.Multiple(() =>
        {
            Assert.That(r.SorenessLocations.Values, Has.Count.EqualTo(2)); // de-duped
            Assert.That(r.Severity, Is.EqualTo(SorenessSeverity.Moderate));
        });
    }

    [Test]
    public void Sleep_ManualScore_IsNotEstimated()
    {
        var s = SleepLog.Create(Day);
        s.SetScore(88, estimated: false);
        Assert.Multiple(() =>
        {
            Assert.That(s.Score, Is.EqualTo(88));
            Assert.That(s.ScoreEstimated, Is.False);
        });
    }

    [Test]
    public void Sleep_ScoreOutOfRange_Throws()
    {
        var s = SleepLog.Create(Day);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.SetScore(120, false));
    }

    [Test]
    public void Measurement_Negative_Throws()
    {
        var m = Measurement.Create(Day);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            m.Update(-1, null, null, null, null, null, null, null, null, null, null, null, null));
    }

    [Test]
    public void Measurement_StoresValues()
    {
        var m = Measurement.Create(Day);
        m.Update(186.3, 37.25, null, null, 14.75, 22.5, 27.2, 88.2, 9, 54, 1648, 53, "shirts looser");
        Assert.Multiple(() =>
        {
            Assert.That(m.WeightLbs, Is.EqualTo(186.3));
            Assert.That(m.MetabolicAge, Is.EqualTo(53));
            Assert.That(m.ClothingFitNotes, Is.EqualTo("shirts looser"));
        });
    }
}

using FitRecoveryLog.Domain.Nutrition;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Nutrition;

[TestFixture]
public class MacrosTests
{
    private static Macros Sample() => new(calories: 200, proteinG: 20, carbsG: 10, fatG: 5,
        fiberG: 3, sugarG: 4, addedSugarG: 2, sodiumMg: 150);

    [Test]
    public void None_HasNothing()
    {
        Assert.That(Macros.None.HasAny, Is.False);
    }

    [Test]
    public void HasAny_TrueWhenAnyComponentPresent()
    {
        var m = new Macros(null, 20, null, null, null, null, null, null);
        Assert.That(m.HasAny, Is.True);
    }

    [Test]
    public void Scale_MultipliesKnownComponents_RoundsCaloriesAndSodium()
    {
        var scaled = Sample().Scale(2.0);
        Assert.Multiple(() =>
        {
            Assert.That(scaled.Calories, Is.EqualTo(400));
            Assert.That(scaled.ProteinG, Is.EqualTo(40));
            Assert.That(scaled.SodiumMg, Is.EqualTo(300));
        });
    }

    [Test]
    public void Scale_LeavesUnknownComponentsUnknown()
    {
        var m = new Macros(calories: 100, proteinG: null, carbsG: null, fatG: null,
            fiberG: null, sugarG: null, addedSugarG: null, sodiumMg: null);
        var scaled = m.Scale(0.5);
        Assert.Multiple(() =>
        {
            Assert.That(scaled.Calories, Is.EqualTo(50));
            Assert.That(scaled.ProteinG, Is.Null);
        });
    }

    [Test]
    public void Scale_NegativeFactor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample().Scale(-1));
    }

    [Test]
    public void Add_UnknownPlusUnknown_StaysUnknown()
    {
        var sum = Macros.None + Macros.None;
        Assert.That(sum.Calories, Is.Null);
    }

    [Test]
    public void Add_UnknownPlusValue_TakesValue()
    {
        var value = new Macros(150, null, null, null, null, null, null, null);
        var sum = Macros.None + value;
        Assert.That(sum.Calories, Is.EqualTo(150));
    }

    [Test]
    public void Add_ValuePlusValue_SumsComponents()
    {
        var sum = Sample() + Sample();
        Assert.Multiple(() =>
        {
            Assert.That(sum.Calories, Is.EqualTo(400));
            Assert.That(sum.ProteinG, Is.EqualTo(40));
        });
    }

    [Test]
    public void Negative_Component_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Macros(-5, null, null, null, null, null, null, null));
    }

    [Test]
    public void Equality_IsByValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Sample(), Is.EqualTo(Sample()));
            Assert.That(Sample() == Sample(), Is.True);
            Assert.That(Sample().GetHashCode(), Is.EqualTo(Sample().GetHashCode()));
            Assert.That(Sample(), Is.Not.EqualTo(Sample().Scale(2)));
        });
    }
}

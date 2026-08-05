using FitRecoveryLog.Domain.Nutrition;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Nutrition;

[TestFixture]
public class DrinkTests
{
    [Test]
    public void SetOunces_Negative_Throws()
    {
        var d = Drink.Create(DateTime.Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => d.SetOunces(-1));
    }

    [Test]
    public void SetSugarCount_Negative_Throws()
    {
        var d = Drink.Create(DateTime.Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => d.SetSugarCount(-2));
    }

    [Test]
    public void Create_TrimsDescription_AndAllowsAmounts()
    {
        var d = Drink.Create(DateTime.Now, "  Cold brew ");
        d.SetOunces(16);
        d.SetSugarCount(1);
        Assert.Multiple(() =>
        {
            Assert.That(d.Description, Is.EqualTo("Cold brew"));
            Assert.That(d.Ounces, Is.EqualTo(16));
            Assert.That(d.SugarCount, Is.EqualTo(1));
        });
    }
}

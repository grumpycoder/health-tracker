using FitRecoveryLog.Data;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Nutrition;

[TestFixture]
public class EnergyMathTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Test]
    public void CompletedDay_WithHealthEnergy_UsesRestingPlusActive_WithoutWorkoutBurn()
    {
        var day = new DailyLog { Date = Yesterday, RestingEnergyKcal = 1900, ActiveEnergyKcal = 650 };
        var result = EnergyMath.Out(day, Today, bmr: 1852, weightLbs: 200, workoutSeconds: 3600);
        Assert.That(result, Is.EqualTo(new EnergyMath.EnergyOut(2550, true)));
    }

    [Test]
    public void Today_IgnoresHealthEnergy_AndUsesEstimate()
    {
        var day = new DailyLog { Date = Today, RestingEnergyKcal = 600, ActiveEnergyKcal = 100 };
        var result = EnergyMath.Out(day, Today, bmr: 1852, weightLbs: null, workoutSeconds: 0);
        Assert.That(result, Is.EqualTo(new EnergyMath.EnergyOut(2315, false)));
    }

    [Test]
    public void MissingActiveEnergy_FallsBackToBmrPlusWorkout()
    {
        var day = new DailyLog { Date = Yesterday, RestingEnergyKcal = 1900 };
        // 60 min × 5 MET × 3.5 × 90.7184 kg / 200 ≈ 476
        var result = EnergyMath.Out(day, Today, bmr: 1852, weightLbs: 200, workoutSeconds: 3600);
        Assert.That(result, Is.EqualTo(new EnergyMath.EnergyOut(2315 + 476, false)));
    }

    [Test]
    public void NoBmrOrWeight_ReturnsNull() =>
        Assert.That(EnergyMath.Out(null, Today, null, null, 0), Is.Null);
}

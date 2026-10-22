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

[TestFixture]
public class EnergyHistoryTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Test]
    public void Average_UsesCompletedDaysOnly_AndCountsHealthDays()
    {
        var logs = new[]
        {
            new DailyLog { Date = Today, RestingEnergyKcal = 500, ActiveEnergyKcal = 100 },          // today: ignored
            new DailyLog { Date = Today.AddDays(-1), RestingEnergyKcal = 1900, ActiveEnergyKcal = 700 }, // 2600
            new DailyLog { Date = Today.AddDays(-2), RestingEnergyKcal = 1900, ActiveEnergyKcal = 500 }, // 2400
        };
        var body = new[] { new BodyMeasurement { Date = Today.AddDays(-30), BasalMetabolicRate = 1800 } }; // est 2250
        var h = new EnergyHistory(logs, body, [], Today);

        var avg = h.Average(Today, 3);   // yesterday, -2, -3 → 7250 / 3 = 2416.7
        Assert.That(avg, Is.EqualTo(new EnergyHistory.RollingAverage(2417, 3, 2)));
    }

    [Test]
    public void For_UsesBodyNumbersKnownAsOfThatDay()
    {
        var body = new[]
        {
            new BodyMeasurement { Date = Today.AddDays(-10), BasalMetabolicRate = 1600 },
            new BodyMeasurement { Date = Today.AddDays(-1), BasalMetabolicRate = 2000 },
        };
        var h = new EnergyHistory([], body, [], Today);
        Assert.Multiple(() =>
        {
            Assert.That(h.For(Today.AddDays(-5))?.Kcal, Is.EqualTo(2000));   // 1600 × 1.25
            Assert.That(h.For(Today)?.Kcal, Is.EqualTo(2500));               // 2000 × 1.25
            Assert.That(h.For(Today.AddDays(-20)), Is.Null);                 // before any measurement
        });
    }
}

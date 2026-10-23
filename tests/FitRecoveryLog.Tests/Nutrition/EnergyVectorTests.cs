using System.Text.Json;
using FitRecoveryLog.Data;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Nutrition;

/// <summary>Runs the shared cases in tests/vectors/energy.json — the Swift app runs the same file.</summary>
[TestFixture]
public class EnergyVectorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record OutCase(DateOnly Day, int? Kcal, bool FromHealth, bool Live);
    private sealed record AvgCase(DateOnly End, int Window, int Kcal, int Days, int HealthDays);
    private sealed record BalCase(DateOnly End, int Window, int AvgNet, int TotalNet, int Days);
    private sealed record VectorCase(string Name, DateOnly Today, List<DailyLog> Logs, List<BodyMeasurement> Body,
        List<WorkoutSession> Sessions, Dictionary<DateOnly, int>? CaloriesIn,
        List<OutCase>? ExpectOut, List<AvgCase>? ExpectAverage, List<BalCase>? ExpectBalance);
    private sealed record VectorFile(List<VectorCase> Cases);

    private static IEnumerable<TestCaseData> Cases()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "vectors", "energy.json");
        var file = JsonSerializer.Deserialize<VectorFile>(File.ReadAllText(path), Json)!;
        return file.Cases.Select(c => new TestCaseData(c).SetName(c.Name));
    }

    [TestCaseSource(nameof(Cases))]
    public void Matches(object boxed)
    {
        var c = (VectorCase)boxed;
        var h = new EnergyHistory(c.Logs, c.Body, c.Sessions, c.Today);
        Assert.Multiple(() =>
        {
            foreach (var e in c.ExpectOut ?? [])
            {
                var got = h.For(e.Day);
                if (e.Kcal is null) Assert.That(got, Is.Null, $"out {e.Day}");
                else Assert.That(got, Is.EqualTo(new EnergyMath.EnergyOut(e.Kcal.Value, e.FromHealth, e.Live)), $"out {e.Day}");
            }
            foreach (var e in c.ExpectAverage ?? [])
                Assert.That(h.Average(e.End, e.Window), Is.EqualTo(new EnergyHistory.RollingAverage(e.Kcal, e.Days, e.HealthDays)));
            foreach (var e in c.ExpectBalance ?? [])
                Assert.That(h.Balance(e.End, e.Window, c.CaloriesIn ?? new()),
                    Is.EqualTo(new EnergyHistory.RollingBalance(e.AvgNet, e.TotalNet, e.Days)));
        });
    }
}

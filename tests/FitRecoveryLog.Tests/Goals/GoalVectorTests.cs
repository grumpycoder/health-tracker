using System.Text.Json;
using FitRecoveryLog.Data;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Goals;

/// <summary>Runs tests/vectors/goals.json — the Swift app runs the same file.</summary>
[TestFixture]
public class GoalVectorTests
{
    private sealed record Expect(string Label, int? Percent, bool Met, string Trend, string Arrow, string Status,
        string Achieved, string Recent, string RecentTrend);
    private sealed record VectorFile(GoalSettings Goals, List<BodyMeasurement> History, List<Expect> Expect);

    [Test]
    public void OverviewMatchesSharedVectors()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "vectors", "goals.json");
        var f = JsonSerializer.Deserialize<VectorFile>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var rows = GoalProgress.Overview(f.Goals, f.History.Select(GoalProgress.From).ToList());
        Assert.Multiple(() =>
        {
            foreach (var e in f.Expect)
            {
                var r = rows.Single(x => x.Label == e.Label);
                Assert.That((r.Percent, r.Met, r.Trend.ToString().ToLowerInvariant(), r.Arrow, r.Status, r.Achieved, r.Recent,
                        r.RecentTrend.ToString().ToLowerInvariant()),
                    Is.EqualTo((e.Percent, e.Met, e.Trend, e.Arrow, e.Status, e.Achieved, e.Recent, e.RecentTrend)), e.Label);
            }
        });
    }
}

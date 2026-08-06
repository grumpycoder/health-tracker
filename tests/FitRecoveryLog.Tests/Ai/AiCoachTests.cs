using FitRecoveryLog.Application.Ai;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Ai;

/// <summary>Unit tests for the shared AiCoach against a stub LLM transport — proving the coach
/// is provider-agnostic (no Gemini needed) and parses responses into the DTOs.</summary>
[TestFixture]
public class AiCoachTests
{
    [Test]
    public async Task SuggestMealTags_ParsesTagsAndStars_MappingToVocabularyCasing()
    {
        var llm = new StubLlm("""{ "tags": ["high protein"], "newTag": "Air fried", "stars": 4, "starReason": "solid" }""");
        var coach = new AiCoach(llm, new StubSettings(), new StubData());

        var result = await coach.SuggestMealTagsAsync("Lunch", "Grilled chicken bowl", null,
            new[] { "High protein", "High sugar" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Known, Does.Contain("High protein")); // canonical casing from vocabulary
            Assert.That(result.Stars, Is.EqualTo(4));
            Assert.That(result.Proposed, Is.EqualTo("Air fried")); // genuinely new tag kept
        });
    }

    [Test]
    public async Task SuggestWorkload_KeepsOnlyKnownAreas()
    {
        var llm = new StubLlm("""{ "intensity": "Heavy", "areas": ["lower back", "unknown area"], "worthLogging": true, "note": "log it" }""");
        var coach = new AiCoach(llm, new StubSettings(), new StubData());

        var s = await coach.SuggestWorkloadAsync("moving boxes", 30, null, new[] { "Lower back", "Legs" });

        Assert.Multiple(() =>
        {
            Assert.That(s.Intensity, Is.EqualTo("Heavy"));
            Assert.That(s.Areas, Is.EqualTo(new[] { "Lower back" })); // mapped to vocab; unknown dropped
            Assert.That(s.WorthLogging, Is.True);
        });
    }

    [Test]
    public async Task IsConfigured_DelegatesToTransport()
    {
        Assert.That(await new AiCoach(new StubLlm("", configured: false), new StubSettings(), new StubData()).IsConfiguredAsync(), Is.False);
        Assert.That(await new AiCoach(new StubLlm("", configured: true), new StubSettings(), new StubData()).IsConfiguredAsync(), Is.True);
    }

    [Test]
    public async Task DailyCheck_ParsesToneAndTips()
    {
        var llm = new StubLlm("""{ "tone": "good", "synopsis": "Solid day so far.", "tips": ["hydrate", "protein at lunch"] }""");
        var check = await new AiCoach(llm, new StubSettings(), new StubData()).DailyCheckAsync();

        Assert.Multiple(() =>
        {
            Assert.That(check.Tone, Is.EqualTo("good"));
            Assert.That(check.Synopsis, Is.EqualTo("Solid day so far."));
            Assert.That(check.Tips, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task AdviseMeal_ParsesVerdict()
    {
        var llm = new StubLlm("""{ "verdict": "reconsider", "reason": "second restaurant meal", "restaurantAlternative": "grilled bowl", "homemadeAlternative": null }""");
        var advice = await new AiCoach(llm, new StubSettings(), new StubData()).AdviseMealAsync("pizza");

        Assert.That(advice, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(advice!.Verdict, Is.EqualTo("reconsider"));
            Assert.That(advice.RestaurantAlt, Is.EqualTo("grilled bowl"));
            Assert.That(advice.HomemadeAlt, Is.Null);
        });
    }

    private sealed class StubLlm : ILlmClient
    {
        private readonly string _response;
        private readonly bool _configured;
        public StubLlm(string response, bool configured = true) { _response = response; _configured = configured; }
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(_configured);
        public Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default) =>
            Task.FromResult<string?>(_response);
    }

    private sealed class StubSettings : IAiSettings
    {
        public string? CoachingGoals => null;
        public bool IncludeCessationData => false;
        public MacroTargets MacroTargets => MacroTargets.None;
    }

    private sealed class StubData : IAiDataProvider
    {
        public Task<string> GetTodayContextAsync(bool includeCessation, CancellationToken ct = default) =>
            Task.FromResult("(no data)");
    }
}

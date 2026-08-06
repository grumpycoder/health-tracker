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
        var coach = new AiCoach(llm, new StubSettings());

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
        var coach = new AiCoach(llm, new StubSettings());

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
        Assert.That(await new AiCoach(new StubLlm("", configured: false), new StubSettings()).IsConfiguredAsync(), Is.False);
        Assert.That(await new AiCoach(new StubLlm("", configured: true), new StubSettings()).IsConfiguredAsync(), Is.True);
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
    }
}

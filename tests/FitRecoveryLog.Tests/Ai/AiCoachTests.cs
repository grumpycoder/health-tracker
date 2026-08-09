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
        var coach = new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts());

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
    public async Task EstimateMealFromText_ParsesMacrosAndTags()
    {
        var llm = new StubLlm("""{ "foodDescription": "a medium orange", "servingSize": "1 medium", "calories": 62, "proteinG": 1.2, "carbsG": 15.4, "sugarG": 12.2, "addedSugarG": 0, "fatG": 0.2, "sodiumMg": 0, "fiberG": 3.1, "tags": ["fruit"], "newTag": null, "stars": 5, "starReason": "whole fruit, great" }""");
        var coach = new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts());

        var scan = await coach.EstimateMealFromTextAsync("Snack", "an orange", null, new[] { "Fruit" });

        Assert.That(scan, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(scan!.Facts.Calories, Is.EqualTo(62));
            Assert.That(scan.Facts.SugarG, Is.EqualTo(12.2));
            Assert.That(scan.Facts.FiberG, Is.EqualTo(3.1));
            Assert.That(scan.Tags.Known, Does.Contain("Fruit")); // mapped to vocabulary casing
            Assert.That(scan.Tags.Stars, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task EstimateMealFromText_ParsesPortionAdvice_WhenOverTargets()
    {
        var llm = new StubLlm("""{ "foodDescription": "chicken and rice", "calories": 800, "carbsG": 100, "proteinG": 30, "tags": [], "stars": 3, "portionAdvice": { "needed": true, "message": "Carbs already high today", "suggestion": "halve the rice", "adjusted": { "calories": 650, "carbsG": 72, "proteinG": 30 } } }""");
        var scan = await new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts())
            .EstimateMealFromTextAsync("Dinner", "chicken and rice", null, Array.Empty<string>());

        Assert.That(scan, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(scan!.Advice, Is.Not.Null);
            Assert.That(scan.Advice!.Suggestion, Is.EqualTo("halve the rice"));
            Assert.That(scan.Advice.Adjusted.Calories, Is.EqualTo(650));
            Assert.That(scan.Advice.Adjusted.CarbsG, Is.EqualTo(72));
        });
    }

    [Test]
    public async Task EstimateMealFromText_NoAdvice_WhenFits()
    {
        var llm = new StubLlm("""{ "calories": 300, "tags": [], "stars": 4, "portionAdvice": { "needed": false, "message": "", "suggestion": "", "adjusted": { "calories": 300 } } }""");
        var scan = await new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts())
            .EstimateMealFromTextAsync("Snack", "an apple", null, Array.Empty<string>());
        Assert.That(scan!.Advice, Is.Null);
    }

    [Test]
    public async Task EstimateMealFromText_EmptyDescription_ReturnsNull()
    {
        var scan = await new AiCoach(new StubLlm(""), new StubSettings(), new StubData(), new StubPrompts())
            .EstimateMealFromTextAsync("Snack", "   ", null, Array.Empty<string>());
        Assert.That(scan, Is.Null);
    }

    [Test]
    public async Task SuggestWorkload_KeepsOnlyKnownAreas()
    {
        var llm = new StubLlm("""{ "intensity": "Heavy", "areas": ["lower back", "unknown area"], "worthLogging": true, "note": "log it" }""");
        var coach = new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts());

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
        Assert.That(await new AiCoach(new StubLlm("", configured: false), new StubSettings(), new StubData(), new StubPrompts()).IsConfiguredAsync(), Is.False);
        Assert.That(await new AiCoach(new StubLlm("", configured: true), new StubSettings(), new StubData(), new StubPrompts()).IsConfiguredAsync(), Is.True);
    }

    [Test]
    public async Task DailyCheck_ParsesToneAndTips()
    {
        var llm = new StubLlm("""{ "tone": "good", "synopsis": "Solid day so far.", "tips": ["hydrate", "protein at lunch"] }""");
        var check = await new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts()).DailyCheckAsync();

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
        var advice = await new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts()).AdviseMealAsync("pizza");

        Assert.That(advice, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(advice!.Verdict, Is.EqualTo("reconsider"));
            Assert.That(advice.RestaurantAlt, Is.EqualTo("grilled bowl"));
            Assert.That(advice.HomemadeAlt, Is.Null);
        });
    }

    [Test]
    public async Task Analyze_ParsesAnalysisExercisesAndBodyTrend()
    {
        var llm = new StubLlm("""
{ "analysis": "Solid 8 weeks.", "exercises": [{ "name": "Squat", "action": "PROGRESS", "target": "3x12" }],
  "topActions": ["add a pull day"], "mealFlags": [], "bodyTrend": { "status": "on-track", "note": "steady" } }
""");
        var outcome = await new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts()).AnalyzeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Analysis, Is.EqualTo("Solid 8 weeks."));
            Assert.That(outcome.Exercises, Has.Count.EqualTo(1));
            Assert.That(outcome.Exercises[0].Action, Is.EqualTo("progress")); // lowercased
            Assert.That(outcome.TopActions, Has.Count.EqualTo(1));
            Assert.That(outcome.BodyTrendStatus, Is.EqualTo("on-track"));
        });
    }

    [Test]
    public async Task SuggestRoutine_ParsesDraft()
    {
        var llm = new StubLlm("""
{ "name": "Push", "rationale": "chest is low", "exercises": [
  { "name": "Push-ups", "isNew": false, "muscles": "chest/triceps", "measure": "reps", "sets": 3, "reps": 12, "restSeconds": 60 } ] }
""");
        var draft = await new AiCoach(llm, new StubSettings(), new StubData(), new StubPrompts()).SuggestRoutineAsync(hint: null);

        Assert.That(draft, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(draft!.Name, Is.EqualTo("Push"));
            Assert.That(draft.Exercises, Has.Count.EqualTo(1));
            Assert.That(draft.Exercises[0].Sets, Is.EqualTo(3));
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
        public Task<string> GetEightWeekContextAsync(CancellationToken ct = default) => Task.FromResult("(no data)");
        public Task<string> GetRoutineDesignContextAsync(CancellationToken ct = default) => Task.FromResult("(no data)");
        public Task<IReadOnlyCollection<string>> RecentlyEasyExercisesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(new List<string>());
    }

    // Returns the embedded default (no stored override) — the coach's fallback path.
    private sealed class StubPrompts : IPromptStore
    {
        public Task<string> GetTemplateAsync(string key, string fallback, CancellationToken ct = default) =>
            Task.FromResult(fallback);
    }
}

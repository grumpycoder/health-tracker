using System.Text;
using System.Text.Json;
using FitRecoveryLog.Domain.Nutrition;

namespace FitRecoveryLog.Application.Ai;

/// <summary>
/// Shared implementation of <see cref="IAiCoach"/>: provider- and client-agnostic prompt
/// building + response parsing over the <see cref="ILlmClient"/> transport. The phone and web
/// both use it. Data-driven features move here next (via a per-client data-provider port); for
/// now it hosts the features whose prompts are built purely from their parameters.
/// </summary>
public sealed class AiCoach : IAiCoach
{
    private readonly ILlmClient _llm;
    private readonly IAiSettings _settings;
    private readonly IAiDataProvider _data;
    private readonly IPromptStore _prompts;

    public AiCoach(ILlmClient llm, IAiSettings settings, IAiDataProvider data, IPromptStore prompts)
    {
        _llm = llm;
        _settings = settings;
        _data = data;
        _prompts = prompts;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _llm.IsConfiguredAsync(ct);

    public async Task<DailyCheck> DailyCheckAsync(CancellationToken ct = default)
    {
        var partOfDay = DateTime.Now.Hour switch
        {
            < 5 => "the middle of the night",
            < 12 => "the morning",
            < 17 => "the afternoon",
            < 21 => "the evening",
            _ => "late at night",
        };
        // Editable template (falls back to the embedded default); the coach fills the placeholders
        // and appends the dynamic goals/targets/context after it.
        var template = await _prompts.GetTemplateAsync(PromptDefaults.DailyCheckKey, PromptDefaults.DailyCheck, ct);

        var sb = new StringBuilder();
        sb.AppendLine(template.Replace("{timeOfDay}", partOfDay).Replace("{dayOfWeek}", DateTime.Now.DayOfWeek.ToString()));
        AppendUserGoals(sb);
        AppendMacroGoalTargets(sb, _settings.MacroTargets);
        sb.AppendLine();
        sb.Append(await _data.GetTodayContextAsync(_settings.IncludeCessationData, ct));

        var text = await _llm.GenerateJsonAsync(sb.ToString(), ct: ct);
        if (string.IsNullOrWhiteSpace(text))
            return new("mixed", "The AI returned an empty response.", new());
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var tone = root.TryGetProperty("tone", out var t) ? t.GetString()?.ToLowerInvariant() ?? "mixed" : "mixed";
            var synopsis = root.TryGetProperty("synopsis", out var s) ? s.GetString() ?? "" : text;
            var tips = new List<string>();
            if (root.TryGetProperty("tips", out var arr) && arr.ValueKind == JsonValueKind.Array)
                tips.AddRange(arr.EnumerateArray().Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!.Cast<string>());
            return new(tone, synopsis.Trim(), tips);
        }
        catch (JsonException)
        {
            return new("mixed", text.Trim(), new());
        }
    }

    public async Task<MealAdvice?> AdviseMealAsync(string considering, CancellationToken ct = default)
    {
        var template = await _prompts.GetTemplateAsync(PromptDefaults.AdviseMealKey, PromptDefaults.AdviseMeal, ct);
        var sb = new StringBuilder();
        sb.AppendLine(template);
        AppendUserGoals(sb);
        sb.AppendLine();
        sb.AppendLine($"CONSIDERING: \"{considering.Trim()}\"");
        sb.AppendLine();
        sb.Append(await _data.GetTodayContextAsync(_settings.IncludeCessationData, ct));

        var text = await _llm.GenerateJsonAsync(sb.ToString(), ct: ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            return new(
                root.TryGetProperty("verdict", out var v) ? v.GetString()?.ToLowerInvariant() ?? "fine" : "fine",
                root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "",
                root.TryGetProperty("restaurantAlternative", out var ra) ? ra.GetString() : null,
                root.TryGetProperty("homemadeAlternative", out var ha) ? ha.GetString() : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<AiOutcome> AnalyzeAsync(CancellationToken ct = default)
    {
        var template = await _prompts.GetTemplateAsync(PromptDefaults.AnalyzeKey, PromptDefaults.Analyze, ct);
        var sb = new StringBuilder();
        sb.AppendLine(template);
        AppendUserGoals(sb);
        AppendMacroGoalTargets(sb, _settings.MacroTargets);
        sb.AppendLine();
        sb.Append(await _data.GetEightWeekContextAsync(ct));

        var text = await _llm.GenerateJsonAsync(sb.ToString(), ct: ct);
        if (string.IsNullOrWhiteSpace(text))
            return new("The AI returned an empty response.", new(), new(), new(), null, null);
        try
        {
            using var outDoc = JsonDocument.Parse(text);
            var root = outDoc.RootElement;
            var analysis = root.TryGetProperty("analysis", out var a) ? a.GetString() ?? "" : text;

            var exercises = new List<ExerciseAdvice>();
            if (root.TryGetProperty("exercises", out var exArr) && exArr.ValueKind == JsonValueKind.Array)
                foreach (var e in exArr.EnumerateArray())
                {
                    var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    exercises.Add(new(name,
                        e.TryGetProperty("action", out var act) ? act.GetString()?.ToLowerInvariant() ?? "hold" : "hold",
                        e.TryGetProperty("target", out var t) ? t.GetString() : null));
                }

            var actions = StringList(root, "topActions");
            var mealFlags = StringList(root, "mealFlags");

            string? bodyStatus = null, bodyNote = null;
            if (root.TryGetProperty("bodyTrend", out var bt) && bt.ValueKind == JsonValueKind.Object)
            {
                bodyStatus = bt.TryGetProperty("status", out var st) ? st.GetString()?.ToLowerInvariant() : null;
                bodyNote = bt.TryGetProperty("note", out var nt) ? nt.GetString() : null;
            }

            return new(analysis.Trim(), exercises, actions, mealFlags, bodyStatus, bodyNote);
        }
        catch (JsonException)
        {
            return new(text.Trim(), new(), new(), new(), null, null);
        }

        static List<string> StringList(JsonElement root, string prop)
        {
            var list = new List<string>();
            if (root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                list.AddRange(arr.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))!
                    .Cast<string>());
            return list;
        }
    }

    public Task<IReadOnlyCollection<string>> RecentlyEasyExercisesAsync(CancellationToken ct = default) =>
        _data.RecentlyEasyExercisesAsync(ct);

    public async Task<RoutineSuggestion?> SuggestRoutineAsync(string? hint, bool bodyweightOnly = true, CancellationToken ct = default)
    {
        var equipment = bodyweightOnly
            ? "EQUIPMENT — STRICT: every exercise must be doable with bodyweight alone (a mat/floor/wall is fine). No dumbbells, bands, bars, benches, or machines."
            : "EQUIPMENT: common home equipment is OK (dumbbells, bands, pull-up bar); prefer what the library's equipment notes already show.";
        var template = await _prompts.GetTemplateAsync(PromptDefaults.SuggestRoutineKey, PromptDefaults.SuggestRoutine, ct);

        var sb = new StringBuilder();
        sb.AppendLine(template
            .Replace("{muscleCatalog}", string.Join(", ", MuscleCatalog))
            .Replace("{equipment}", equipment));
        if (!string.IsNullOrWhiteSpace(hint)) sb.AppendLine($"USER REQUEST (honor this): {hint.Trim()}");
        sb.AppendLine();
        sb.Append(await _data.GetRoutineDesignContextAsync(ct));

        var text = await _llm.GenerateJsonAsync(sb.ToString(), ct: ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
            var rationale = root.TryGetProperty("rationale", out var ra) ? ra.GetString() ?? "" : "";
            var list = new List<SuggestedExercise>();
            if (root.TryGetProperty("exercises", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                {
                    var exName = e.TryGetProperty("name", out var en) ? en.GetString()?.Trim() : null;
                    if (string.IsNullOrWhiteSpace(exName)) continue;
                    list.Add(new(
                        exName,
                        e.TryGetProperty("isNew", out var inw) && inw.ValueKind == JsonValueKind.True,
                        e.TryGetProperty("muscles", out var mu) ? mu.GetString() : null,
                        e.TryGetProperty("measure", out var me) && me.GetString()?.ToLowerInvariant() == "duration" ? "duration" : "reps",
                        Math.Clamp(IntOrNull(e, "sets") ?? 3, 1, 10),
                        IntOrNull(e, "reps"),
                        IntOrNull(e, "durationSeconds"),
                        IntOrNull(e, "restSeconds")));
                }
            return list.Count == 0 ? null : new(string.IsNullOrEmpty(name) ? "AI Routine" : name, rationale.Trim(), list);
        }
        catch (JsonException)
        {
            return null;
        }

        static int? IntOrNull(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }

    /// <summary>Muscle-group vocabulary the routine prompt asks the model to tag exercises with —
    /// must match the exercise library's picker (and the data provider's volume grouping).</summary>
    private static readonly string[] MuscleCatalog =
        { "Chest", "Back", "Shoulders", "Biceps", "Triceps", "Forearms", "Core", "Glutes", "Quads", "Hamstrings", "Calves" };

    private static void AppendMacroGoalTargets(StringBuilder sb, MacroTargets t)
    {
        sb.AppendLine("DAILY MACRO/HYDRATION TARGETS (ranges are goals, not hard limits — a day slightly " +
                      "under/over is fine; flag only consistent misses). Calories, carbs, and water have " +
                      "separate rest-day and workout-day ranges — use the one matching today's day type:");
        if (t.Protein.IsSet) sb.AppendLine($"  - Protein: {t.Protein.Min}-{t.Protein.Max} g");
        if (t.Fat.IsSet) sb.AppendLine($"  - Fat: {t.Fat.Min}-{t.Fat.Max} g");
        if (t.Fiber.IsSet) sb.AppendLine($"  - Fiber: {t.Fiber.Min}-{t.Fiber.Max} g");
        sb.AppendLine($"  - Added sugar: stay under {t.AddedSugarMax} g (less is better)");
        Split("Calories", t.CaloriesRest, t.CaloriesActive, "");
        Split("Carbohydrates", t.CarbsRest, t.CarbsActive, " g");
        Split("Fluids (all drinks)", t.WaterRest, t.WaterActive, " oz");

        void Split(string label, MacroRange rest, MacroRange act, string unit)
        {
            if (rest.IsSet || act.IsSet)
                sb.AppendLine($"  - {label}: rest-day {rest.Min}-{rest.Max}{unit}, workout-day {act.Min}-{act.Max}{unit}");
        }
    }

    public async Task<WorkloadSuggestion> SuggestWorkloadAsync(string activity, int? minutes, string? notes,
        IReadOnlyList<string> areaVocabulary, CancellationToken ct = default)
    {
        var template = await _prompts.GetTemplateAsync(PromptDefaults.SuggestWorkloadKey, PromptDefaults.SuggestWorkload, ct);
        var sb = new StringBuilder();
        sb.AppendLine(template);
        sb.AppendLine($"Body areas (use these exact strings): {string.Join(" | ", areaVocabulary)}");
        sb.AppendLine();
        sb.AppendLine($"ACTIVITY: \"{activity}\"" + (minutes is { } m ? $" {m}min" : "") +
                      (string.IsNullOrWhiteSpace(notes) ? "" : $" notes:\"{notes}\""));

        var text = await _llm.GenerateJsonAsync(sb.ToString(), ct: ct);
        if (string.IsNullOrWhiteSpace(text)) return new(null, new(), true, null);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var canon = areaVocabulary.ToDictionary(v => v, v => v, StringComparer.OrdinalIgnoreCase);
            var areas = new List<string>();
            if (root.TryGetProperty("areas", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                    if (a.GetString() is { } s && canon.TryGetValue(s.Trim(), out var c) && !areas.Contains(c))
                        areas.Add(c);
            return new(
                root.TryGetProperty("intensity", out var i) ? i.GetString() : null,
                areas,
                !root.TryGetProperty("worthLogging", out var w) || w.ValueKind != JsonValueKind.False,
                root.TryGetProperty("note", out var n) ? n.GetString() : null);
        }
        catch (JsonException)
        {
            return new(null, new(), true, null);
        }
    }

    public async Task<TagSuggestion> SuggestMealTagsAsync(string mealType, string description, string? portionNote,
        IReadOnlyList<string> vocabulary, string? macros = null, CancellationToken ct = default)
    {
        var template = await _prompts.GetTemplateAsync(PromptDefaults.SuggestMealTagsKey, PromptDefaults.SuggestMealTags, ct);
        var sb = new StringBuilder();
        sb.AppendLine(template);
        await AppendMealTaggingRulesAsync(sb, vocabulary, ct);
        sb.AppendLine();
        sb.AppendLine($"MEAL: {mealType} \"{description}\"" +
                      (string.IsNullOrWhiteSpace(portionNote) ? "" : $" portion:\"{portionNote}\""));
        if (!string.IsNullOrWhiteSpace(macros))
        {
            sb.AppendLine($"MACROS (actual, as eaten, from the nutrition label): {macros}");
            sb.AppendLine("These are real measured numbers — base sugar/sodium/protein tags and the star rating on " +
                          "them, not on guesses from the name. Apply the ≤15g-sugar proportion rule to the real sugar figure.");
        }

        var text = await _llm.GenerateJsonAsync(sb.ToString(), ct: ct);
        if (string.IsNullOrWhiteSpace(text)) return new(new(), null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(text);
            return ParseTagSuggestion(doc.RootElement, vocabulary);
        }
        catch (JsonException)
        {
            return new(new(), null, null, null);
        }
    }

    public async Task<MealScan?> ReadNutritionLabelAsync(byte[] imageJpeg, IReadOnlyList<string> vocabulary,
        CancellationToken ct = default)
    {
        var template = await _prompts.GetTemplateAsync(PromptDefaults.ReadLabelKey, PromptDefaults.ReadLabel, ct);
        var sb = new StringBuilder();
        sb.AppendLine(template);
        await AppendMealTaggingRulesAsync(sb, vocabulary, ct);

        var text = await _llm.GenerateJsonAsync(sb.ToString(), imageJpeg, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return new MealScan(ParseFacts(doc.RootElement), ParseTagSuggestion(doc.RootElement, vocabulary));
        }
        catch (JsonException) { return null; }
    }

    public async Task<MealScan?> EstimateMealFromPhotoAsync(byte[] imageJpeg, IReadOnlyList<string> vocabulary,
        string? hint = null, string? mealType = null, CancellationToken ct = default)
    {
        var template = await _prompts.GetTemplateAsync(PromptDefaults.EstimatePhotoKey, PromptDefaults.EstimatePhoto, ct);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(hint))
            sb.AppendLine($"IMPORTANT USER CORRECTION — treat as authoritative and re-estimate accordingly: \"{hint.Trim()}\"");
        sb.AppendLine(template);
        await AppendMealTaggingRulesAsync(sb, vocabulary, ct);
        AppendMacroGoalTargets(sb, _settings.MacroTargets);
        sb.Append(await _data.GetTodayContextAsync(_settings.IncludeCessationData, ct));
        sb.AppendLine($"MEAL TYPE: {(string.IsNullOrWhiteSpace(mealType) ? "(infer from the time in NOW)" : mealType)}");

        var text = await _llm.GenerateJsonAsync(sb.ToString(), imageJpeg, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var facts = ParseFacts(root);
            facts.ServingSize = "whole plate (estimate)";
            return new MealScan(facts, ParseTagSuggestion(root, vocabulary), ParsePortionAdvice(root));
        }
        catch (JsonException) { return null; }
    }

    public async Task<MealScan?> EstimateMealFromTextAsync(string mealType, string description, string? portionNote,
        IReadOnlyList<string> vocabulary, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var template = await _prompts.GetTemplateAsync(PromptDefaults.EstimateTextKey, PromptDefaults.EstimateText, ct);
        var sb = new StringBuilder();
        sb.AppendLine(template);
        await AppendMealTaggingRulesAsync(sb, vocabulary, ct);
        AppendMacroGoalTargets(sb, _settings.MacroTargets);
        sb.Append(await _data.GetTodayContextAsync(_settings.IncludeCessationData, ct));
        sb.AppendLine();
        sb.AppendLine($"MEAL: {mealType} \"{description}\"" +
                      (string.IsNullOrWhiteSpace(portionNote) ? "" : $" portion:\"{portionNote}\""));

        var text = await _llm.GenerateJsonAsync(sb.ToString(), ct: ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            return new MealScan(ParseFacts(root), ParseTagSuggestion(root, vocabulary), ParsePortionAdvice(root));
        }
        catch (JsonException) { return null; }
    }

    // ---- shared prompt helpers ----

    // Shared tagging + star-rating rules (editable via the "meal_tagging_rules" prompt), then the
    // user's goals. Used by the tag suggester, both macro estimators, and the label scanner.
    private async Task AppendMealTaggingRulesAsync(StringBuilder sb, IReadOnlyList<string> vocabulary, CancellationToken ct)
    {
        var template = await _prompts.GetTemplateAsync(PromptDefaults.MealTaggingRulesKey, PromptDefaults.MealTaggingRules, ct);
        sb.AppendLine(template.Replace("{vocabulary}", string.Join(" | ", vocabulary)));
        AppendUserGoals(sb);
    }

    private void AppendUserGoals(StringBuilder sb)
    {
        var goals = _settings.CoachingGoals;
        if (string.IsNullOrWhiteSpace(goals)) return;
        sb.AppendLine("USER'S STATED GOALS & PREFERENCES — these are the success criteria. Coach adherence " +
                      "to THEM; never push toward an implicit ideal (like zero) the user hasn't chosen. " +
                      "EXCEPTION: if a goal is clearly unhealthy or unsafe (crash dieting, dangerous targets, " +
                      "overtraining through pain), do NOT coach toward it — say plainly why, and suggest " +
                      "discussing it with a doctor where appropriate:");
        foreach (var line in goals.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            sb.AppendLine($"  - {line}");
    }

    private static TagSuggestion ParseTagSuggestion(JsonElement root, IReadOnlyList<string> vocabulary)
    {
        int? stars = root.TryGetProperty("stars", out var st) && st.ValueKind == JsonValueKind.Number
            && st.TryGetInt32(out var sv) ? Math.Clamp(sv, 1, 5) : null;
        var starReason = root.TryGetProperty("starReason", out var sr) ? sr.GetString() : null;
        var canon = vocabulary.ToDictionary(v => v, v => v, StringComparer.OrdinalIgnoreCase);
        var known = new List<string>();
        if (root.TryGetProperty("tags", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var t in arr.EnumerateArray())
                if (t.GetString() is { } s && canon.TryGetValue(s.Trim(), out var c) && !known.Contains(c))
                    known.Add(c);
        var proposed = root.TryGetProperty("newTag", out var nt) ? nt.GetString()?.Trim() : null;
        if (!string.IsNullOrWhiteSpace(proposed))
        {
            // Near-duplicate of an existing tag (typo, plural, etc.) → select the real tag.
            var close = vocabulary.FirstOrDefault(v => Levenshtein(v, proposed) <= 2);
            if (close is not null)
            {
                if (!known.Contains(close)) known.Add(close);
                proposed = null;
            }
            else if (canon.ContainsKey(proposed) || IsRedundantTag(proposed))
                proposed = null;
        }
        else proposed = null;
        return new(known, proposed, stars, starReason);
    }

    private static int Levenshtein(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private static bool IsRedundantTag(string tag) =>
        Enum.GetNames<MealType>().Any(t => tag.Contains(t, StringComparison.OrdinalIgnoreCase))
        || new[] { "meal", "drink", "morning", "evening", "late night" }
            .Any(w => tag.Equals(w, StringComparison.OrdinalIgnoreCase));

    // Reads the day-fit advice block. Returns null when the meal fits (or no targets were given),
    // or when there's no actionable suggestion — so the UI only shows a real, applyable trim.
    private static PortionAdvice? ParsePortionAdvice(JsonElement root)
    {
        if (!root.TryGetProperty("portionAdvice", out var pa) || pa.ValueKind != JsonValueKind.Object) return null;
        if (!(pa.TryGetProperty("needed", out var n) && n.ValueKind == JsonValueKind.True)) return null;
        var suggestion = pa.TryGetProperty("suggestion", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(suggestion)) return null;
        var message = pa.TryGetProperty("message", out var m) ? m.GetString() : null;
        var adjusted = pa.TryGetProperty("adjusted", out var adj) && adj.ValueKind == JsonValueKind.Object
            ? ParseFacts(adj) : new NutritionFacts();
        return new PortionAdvice(true, message, suggestion, adjusted);
    }

    private static NutritionFacts ParseFacts(JsonElement r)
    {
        var facts = new NutritionFacts
        {
            ServingSize = SOf(r, "servingSize"), FoodDescription = SOf(r, "foodDescription"),
            Calories = IOf(r, "calories"), ProteinG = DOf(r, "proteinG"), CarbsG = DOf(r, "carbsG"),
            SugarG = DOf(r, "sugarG"), FatG = DOf(r, "fatG"), SodiumMg = IOf(r, "sodiumMg"), FiberG = DOf(r, "fiberG"),
            AddedSugarG = DOf(r, "addedSugarG")
        };
        if (r.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var it in arr.EnumerateArray())
                if (it.ValueKind == JsonValueKind.Object && SOf(it, "name") is { } nm && !string.IsNullOrWhiteSpace(nm))
                    facts.Items.Add(new NutritionItem(nm.Trim(), IOf(it, "calories"), DOf(it, "proteinG"),
                        DOf(it, "carbsG"), DOf(it, "sugarG"), DOf(it, "addedSugarG"), DOf(it, "fatG"),
                        IOf(it, "sodiumMg"), DOf(it, "fiberG")));
        return facts;
    }

    private static int? IOf(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    private static double? DOf(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static string? SOf(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

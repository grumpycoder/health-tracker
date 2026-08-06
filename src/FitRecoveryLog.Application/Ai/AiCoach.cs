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

    public AiCoach(ILlmClient llm, IAiSettings settings, IAiDataProvider data)
    {
        _llm = llm;
        _settings = settings;
        _data = data;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _llm.IsConfiguredAsync(ct);

    public async Task<DailyCheck> DailyCheckAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a supportive but honest health coach doing a quick mid-day check-in on one person's self-tracked day.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""
{
  "tone": "good" | "mixed" | "poor",
  "synopsis": "<2-3 sentences on how the day is going so far and the likely reasons — e.g. possible bloating from high-sodium restaurant food, sugary drinks adding up, short sleep dragging energy, solid workout done. Encouraging when earned, direct when not.>",
  "tips": ["<up to 3 short, actionable suggestions for the REST of today>"]
}
""");
        sb.AppendLine("Consider meal quality/timing, sugary drinks, sleep duration and score, whether a workout happened on a workout day, and physical workload — but JUDGE IN CONTEXT:");
        sb.AppendLine("- Use TODAY'S NOTES for circumstances (travel, events, busy days). A fast-food dinner on a day spent out running errands is life, not failure.");
        sb.AppendLine("- Use LAST 7 DAYS to tell one-off indulgences from patterns. A single off-plan meal in an otherwise solid stretch gets a light touch ('enjoy it, back to normal tomorrow'); direct warnings are for things repeating across several days.");
        sb.AppendLine("- Judge the FOOD, not the venue. A grilled chicken sandwich from a drive-thru is a reasonable protein choice, not a lapse; a burger-and-fries combo is different. Don't penalize 'restaurant/fast food' as a category — eating-out sodium is worth one mention only when frequent.");
        sb.AppendLine("- Zero-sugar drinks (Coke Zero, diet soda, sugar-free) are NOT sugary drinks — taste variety, not a concern.");
        sb.AppendLine("- Keep sugar in PROPORTION: a single small treat/dessert (roughly ≤15g sugar) in an otherwise fine day is normal — don't flag it or suggest 'less sugar'. A banana has ~14g. Only raise sugar when a day's total is genuinely high or it's a daily pattern.");
        AppendUserGoals(sb);
        AppendMacroGoalTargets(sb, _settings.MacroTargets);
        sb.AppendLine("If little is logged yet, say so and suggest what to log.");
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
        var sb = new StringBuilder();
        sb.AppendLine("You are a pragmatic nutrition coach. The user is deciding what to eat NEXT and is considering something specific.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""
{
  "verdict": "good" | "fine" | "reconsider",
  "reason": "<1-2 sentences grounded in TODAY's data and the user's goals, e.g. 'light on protein so far' or 'second restaurant meal today'>",
  "restaurantAlternative": "<a similar-effort restaurant/fast-food option that fits better, or null if the considered choice is already solid>",
  "homemadeAlternative": "<a quick homemade option, or null if homemade isn't realistic for the situation>"
}
""");
        sb.AppendLine("Keep sugar in proportion — a small treat (~15g sugar or less) is normal (a banana has ~14g); " +
                      "don't steer away from it unless the day's sugar is already high.");
        sb.AppendLine("Rules: judge the FOOD, not the venue. Zero-sugar drinks are not a concern. Respect the user's " +
                      "stated goals — never push toward ideals they haven't chosen. Alternatives must be realistic for " +
                      "the same situation (on the road means no homemade — return null). Be brief and practical, not preachy.");
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
        var sb = new StringBuilder();
        sb.AppendLine("You advise on logging one non-workout physical activity in a fitness-recovery tracker.");
        sb.AppendLine("The log's purpose: capture activity that meaningfully taxes the body and affects recovery. Trivial chores are noise.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""{ "intensity": "Light" | "Moderate" | "Heavy", "areas": ["<from the list, only clearly affected>"], "worthLogging": true | false, "note": "<one short practical sentence: why, or how to log it better — e.g. 'Log the basket-carrying up stairs, skip the folding.'>" }""");
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
        var sb = new StringBuilder();
        sb.AppendLine("Tag one logged meal for a personal nutrition tracker, and rate how well it fits the user's goals.");
        sb.AppendLine("Respond with ONLY a JSON object:");
        sb.AppendLine("""{ "tags": ["<existing tags that clearly apply>"], "newTag": "<one new tag ONLY if something important has no existing tag, else null>", "stars": <1-5 how well this meal fits the user's goals below>, "starReason": "<≤8 words, encouraging>" }""");
        AppendMealTaggingRules(sb, vocabulary);
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
        var sb = new StringBuilder();
        sb.AppendLine("Read a packaged-food Nutrition Facts label from this photo AND tag/rate the food. " +
                      "Extract the values for ONE serving as printed. Respond with ONLY this JSON object " +
                      "(null for anything not legible):");
        sb.AppendLine("""{ "servingSize": "<as printed>", "calories": <int>, "proteinG": <num>, "carbsG": <num>, "sugarG": <num>, "addedSugarG": <num>, "fatG": <num>, "sodiumMg": <int>, "fiberG": <num>, "tags": ["<existing tags that apply>"], "newTag": "<one new tag or null>", "stars": <1-5>, "starReason": "<≤8 words, encouraging>" }""");
        sb.AppendLine("Use 'Total Sugars' for sugarG, 'Includes Xg Added Sugars' for addedSugarG (null if not listed), and 'Total Fat' for fatG. Numbers only — strip units. " +
                      "If the image is not a nutrition label, return all nulls. " +
                      "Base tags and the star rating on the ACTUAL macros you read (per serving), not guesses.");
        AppendMealTaggingRules(sb, vocabulary);

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
        string? hint = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(hint))
            sb.AppendLine($"IMPORTANT USER CORRECTION — treat as authoritative and re-estimate accordingly: \"{hint.Trim()}\"");
        sb.AppendLine("Estimate the nutrition of the meal in this photo AND tag/rate it. Identify each food, " +
                      "estimate its portion, then TOTAL the macros for everything on the plate/bowl as shown. " +
                      "Respond with ONLY this JSON object (null for anything you truly can't estimate):");
        sb.AppendLine("""{ "foodDescription": "<short, e.g. 'meatloaf, mashed potatoes, green beans'>", "servingSize": "whole plate (estimate)", "calories": <int>, "proteinG": <num>, "carbsG": <num>, "sugarG": <num>, "addedSugarG": <num>, "fatG": <num>, "sodiumMg": <int>, "fiberG": <num>, "tags": ["<existing tags that apply>"], "newTag": "<one new tag or null>", "stars": <1-5>, "starReason": "<≤8 words, encouraging>" }""");
        sb.AppendLine("Numbers are for the whole plate, units stripped. If it isn't a photo of food, return all nulls.");
        sb.AppendLine("CALIBRATION — photo estimates tend to run HIGH; do NOT overestimate. Assume STANDARD " +
                      "home portions unless the plate is clearly oversized, and do NOT add calories for oil, " +
                      "butter, or sauce you cannot actually see. Use these per-serving anchors and pick the " +
                      "MIDDLE of the range, never the top:");
        sb.AppendLine("  - cooked meat/protein, palm-sized 3-5 oz: 150-300 kcal");
        sb.AppendLine("  - mashed potatoes ~1 cup: 200-250 kcal; rice/pasta ~1 cup: ~200 kcal");
        sb.AppendLine("  - non-starchy veg (green beans, broccoli, salad) ~1 cup: 40-70 kcal");
        sb.AppendLine("  - a slice of bread ~80 kcal; a pat of butter ~35 kcal");
        sb.AppendLine("A normal meat-and-two-sides dinner plate totals roughly 500-750 kcal. Only exceed ~900 " +
                      "if the plate is clearly large or obviously fried, breaded, heavily sauced, or cheesy. " +
                      "Base tags and the star rating on the estimated macros.");
        AppendMealTaggingRules(sb, vocabulary);

        var text = await _llm.GenerateJsonAsync(sb.ToString(), imageJpeg, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var facts = ParseFacts(doc.RootElement);
            facts.ServingSize = "whole plate (estimate)";
            return new MealScan(facts, ParseTagSuggestion(doc.RootElement, vocabulary));
        }
        catch (JsonException) { return null; }
    }

    // ---- shared prompt helpers ----

    private void AppendMealTaggingRules(StringBuilder sb, IReadOnlyList<string> vocabulary)
    {
        sb.AppendLine($"Existing tags (use these exact strings, strongly prefer them): {string.Join(" | ", vocabulary)}");
        sb.AppendLine("Only include tags well supported by the meal; when unsure, leave a tag out. Most meals need " +
                      "just 0-2 tags. Do NOT apply a tag by default — each must clearly fit.");
        sb.AppendLine("'High sodium' specifically: reserve it for foods genuinely high in salt — cured/processed " +
                      "meats (bacon, deli, sausage), canned/instant foods, pizza, chips, or fast-food/restaurant " +
                      "items known to be salt-heavy. A home-cooked rotisserie-chicken/rice/bean bowl or a plain " +
                      "hamburger is NOT automatically high sodium. When unsure, leave it off.");
        sb.AppendLine("Judge the meal AS A WHOLE for sodium — a lean/grilled main (e.g. grilled chicken sandwich) " +
                      "is NOT 'high sodium' just because a side like fries is salty. Only tag High sodium when the " +
                      "meal is PREDOMINANTLY salt-heavy, not when one minor side is.");
        sb.AppendLine("'High sugar' specifically: keep sugar in PROPORTION. A snack or treat with a modest amount " +
                      "(roughly ≤15g sugar — a banana is ~14g, a few graham crackers/cookies are single-digit grams) " +
                      "is NORMAL — do NOT tag 'High sugar' and do NOT lower stars for it. Reserve 'High sugar' for " +
                      "items genuinely loaded with sugar (candy, soda, dessert-sized sweets, ~25g+). When unsure, leave it off.");
        sb.AppendLine("A newTag must be short (1-3 words, e.g. 'High sugar'), broadly reusable, and not a synonym of an existing tag. " +
                      "Tags describe nutritional quality or food source. The entry already records its type " +
                      "(breakfast/lunch/dinner/snack/drink), time, and portion — NEVER suggest those as tags.");
        sb.AppendLine("STARS (1-5) = how well this meal fits the user's goals below, encouraging and moderation-minded: " +
                      "a sensible everyday meal is 3-4; a great goal-aligned choice is 5; a clear off-plan splurge is 1-2. " +
                      "A reasonable treat or modest snack is NOT a failure — a graham-cracker-sized snack with single-digit " +
                      "sugar is a normal 3-4, not a 1-2. Do not dock stars or write a cautionary reason for modest sugar/sodium; " +
                      "reserve low scores for genuinely large portions or truly indulgent items. If no goals are set, rate general balance/protein.");
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

    private static NutritionFacts ParseFacts(JsonElement r)
    {
        int? I(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
        double? D(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new NutritionFacts
        {
            ServingSize = S("servingSize"), FoodDescription = S("foodDescription"),
            Calories = I("calories"), ProteinG = D("proteinG"), CarbsG = D("carbsG"),
            SugarG = D("sugarG"), FatG = D("fatG"), SodiumMg = I("sodiumMg"), FiberG = D("fiberG"),
            AddedSugarG = D("addedSugarG")
        };
    }
}

namespace FitRecoveryLog.Application.Ai;

// Result DTOs for the AI features — the shared vocabulary the coach returns and both the phone
// and web presentation consume. Kept provider-agnostic (no Gemini specifics).

/// <summary>The 8-week analysis outcome: prose plus structured per-exercise advice and flags.</summary>
public sealed record AiOutcome(string Analysis, List<ExerciseAdvice> Exercises, List<string> TopActions,
    List<string> MealFlags, string? BodyTrendStatus, string? BodyTrendNote);

/// <summary>Per-exercise progression advice (progress / hold / backoff + a target).</summary>
public sealed record ExerciseAdvice(string Name, string Action, string? Target);

/// <summary>The daily check-in: an overall tone, a short synopsis, and up to three tips.</summary>
public sealed record DailyCheck(string Tone, string Synopsis, List<string> Tips);

/// <summary>Pre-meal advisor verdict for something the user is considering eating.</summary>
public sealed record MealAdvice(string Verdict, string Reason, string? RestaurantAlt, string? HomemadeAlt);

/// <summary>One exercise in an AI-drafted routine suggestion.</summary>
public sealed record SuggestedExercise(string Name, bool IsNew, string? Muscles, string Measure,
    int Sets, int? Reps, int? DurationSeconds, int? RestSeconds);

/// <summary>An AI-drafted routine: a name, rationale, and its exercises.</summary>
public sealed record RoutineSuggestion(string Name, string Rationale, List<SuggestedExercise> Exercises);

/// <summary>Suggested intensity/areas for a physical-workload entry.</summary>
public sealed record WorkloadSuggestion(string? Intensity, List<string> Areas, bool WorthLogging, string? Note);

/// <summary>Tag suggestions for a meal/drink plus an optional 1-5 goal-fit star rating.</summary>
public sealed record TagSuggestion(List<string> Known, string? Proposed, int? Stars, string? StarReason);

/// <summary>Macros read/estimated from a photo. Mutable so the review UI can bind and edit
/// values before applying them.</summary>
public sealed class NutritionFacts
{
    public string? ServingSize { get; set; }
    public int? Calories { get; set; }
    public double? ProteinG { get; set; }
    public double? CarbsG { get; set; }
    public double? SugarG { get; set; }
    public double? FatG { get; set; }
    public int? SodiumMg { get; set; }
    public double? FiberG { get; set; }
    public double? AddedSugarG { get; set; }
    /// <summary>Foods the AI identified on the plate (plate-estimate mode only).</summary>
    public string? FoodDescription { get; set; }
}

/// <summary>Macros plus tags/star rating from one photo scan — a single AI call
/// instead of scan-then-suggest.</summary>
public sealed record MealScan(NutritionFacts Facts, TagSuggestion Tags);

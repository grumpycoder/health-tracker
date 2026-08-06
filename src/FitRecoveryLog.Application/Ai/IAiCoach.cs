namespace FitRecoveryLog.Application.Ai;

/// <summary>
/// The application-facing AI surface: builds prompts, invokes the LLM through <see cref="ILlmClient"/>,
/// and parses responses into the shared DTOs. Provider- and client-agnostic, so the phone and web
/// share one implementation. Callers never touch the API key — the transport owns it.
/// </summary>
/// <remarks>Currently exposes the parameter-only features (no local data read). The data-driven
/// features (8-week analysis, daily check, meal advice, routine suggestion) move here next, backed
/// by a per-client data-provider port.</remarks>
public interface IAiCoach
{
    /// <summary>Whether AI features should be offered (the provider is configured).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>Mid-day check-in over today's logged data.</summary>
    Task<DailyCheck> DailyCheckAsync(CancellationToken ct = default);

    /// <summary>Pre-meal advisor: judge something the user is considering eating against today.</summary>
    Task<MealAdvice?> AdviseMealAsync(string considering, CancellationToken ct = default);

    Task<WorkloadSuggestion> SuggestWorkloadAsync(string activity, int? minutes, string? notes,
        IReadOnlyList<string> areaVocabulary, CancellationToken ct = default);

    Task<TagSuggestion> SuggestMealTagsAsync(string mealType, string description, string? portionNote,
        IReadOnlyList<string> vocabulary, string? macros = null, CancellationToken ct = default);

    Task<MealScan?> ReadNutritionLabelAsync(byte[] imageJpeg, IReadOnlyList<string> vocabulary,
        CancellationToken ct = default);

    Task<MealScan?> EstimateMealFromPhotoAsync(byte[] imageJpeg, IReadOnlyList<string> vocabulary,
        string? hint = null, CancellationToken ct = default);
}

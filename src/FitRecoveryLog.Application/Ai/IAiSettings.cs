namespace FitRecoveryLog.Application.Ai;

/// <summary>
/// User-configurable inputs that shape AI prompts, behind a port so the shared coach stays out
/// of MAUI Preferences. The phone supplies a Preferences-backed adapter; other clients supply
/// their own. Grows as more prompt inputs (macro targets, opt-ins) migrate off the phone.
/// </summary>
public interface IAiSettings
{
    /// <summary>The user's free-text coaching goals (newline-separated), or null if unset.
    /// Included in prompts as the success criteria the AI coaches toward.</summary>
    string? CoachingGoals { get; }
}

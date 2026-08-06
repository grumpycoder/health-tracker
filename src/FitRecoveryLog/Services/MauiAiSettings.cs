using FitRecoveryLog.Application.Ai;
using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

/// <summary>Preferences-backed <see cref="IAiSettings"/> for the phone. Uses the same key the
/// Settings screen writes, so existing coaching goals carry over.</summary>
public sealed class MauiAiSettings : IAiSettings
{
    public string? CoachingGoals => Preferences.Default.Get<string?>("ai_user_goals", null);
}

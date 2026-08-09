using FitRecoveryLog.Application.Ai;
using FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>
/// Web <see cref="IAiSettings"/> backed by the synced <see cref="GoalSettings"/> singleton (edited on
/// the web Settings page). Reads the last-pulled dataset synchronously via <see cref="AppState.Cached"/>
/// — pages load the pull before invoking the coach — and falls back to neutral defaults when no goals
/// have been set yet, so the AI still works out of the box.
/// </summary>
public sealed class WebAiSettings : IAiSettings
{
    private readonly AppState _state;
    public WebAiSettings(AppState state) => _state = state;

    private GoalSettings? Current
    {
        get
        {
            var pull = _state.Cached;
            return pull is null ? null : WebSyncClient.Rows<GoalSettings>(pull).OrderByDescending(g => g.UpdatedAt).FirstOrDefault();
        }
    }

    public string? CoachingGoals => string.IsNullOrWhiteSpace(Current?.CoachingGoals) ? null : Current!.CoachingGoals;

    public bool IncludeCessationData => Current?.IncludeCessationData ?? false;

    public MacroTargets MacroTargets
    {
        get
        {
            var g = Current;
            if (g is null) return MacroTargets.None;
            return new MacroTargets(
                new MacroRange(g.ProteinMin, g.ProteinMax),
                new MacroRange(g.FatMin, g.FatMax),
                new MacroRange(g.FiberMin, g.FiberMax),
                g.AddedSugarMax,
                new MacroRange(g.CaloriesRestMin, g.CaloriesRestMax),
                new MacroRange(g.CaloriesActiveMin, g.CaloriesActiveMax),
                new MacroRange(g.CarbsRestMin, g.CarbsRestMax),
                new MacroRange(g.CarbsActiveMin, g.CarbsActiveMax),
                new MacroRange(g.WaterRestMin, g.WaterRestMax),
                new MacroRange(g.WaterActiveMin, g.WaterActiveMax));
        }
    }
}

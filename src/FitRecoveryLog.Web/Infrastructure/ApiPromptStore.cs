using FitRecoveryLog.Application.Ai;
using FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>
/// Web <see cref="IPromptStore"/>: reads an edited template from the sync-pull cache. Returns the
/// embedded <paramref name="fallback"/> when there's no override (or on any error), so the coach
/// always has a usable prompt.
/// </summary>
public sealed class ApiPromptStore : IPromptStore
{
    private readonly AppState _state;
    public ApiPromptStore(AppState state) => _state = state;

    public async Task<string> GetTemplateAsync(string key, string fallback, CancellationToken ct = default)
    {
        try
        {
            var row = WebSyncClient.Rows<PromptTemplate>(await _state.DataAsync()).FirstOrDefault(p => p.PromptKey == key);
            return string.IsNullOrWhiteSpace(row?.Text) ? fallback : row!.Text;
        }
        catch { return fallback; }
    }
}

namespace FitRecoveryLog.Application.Ai;

/// <summary>
/// Supplies the (optionally user-edited) template text for a prompt. Behind a port so the shared
/// coach doesn't know whether templates come from EF (phone) or the sync cache (web). Always
/// returns something: the stored override when present and non-blank, else the embedded default.
/// </summary>
public interface IPromptStore
{
    /// <param name="key">Stable prompt id (see <see cref="PromptDefaults"/>).</param>
    /// <param name="fallback">Embedded default used when no usable override is stored.</param>
    Task<string> GetTemplateAsync(string key, string fallback, CancellationToken ct = default);
}

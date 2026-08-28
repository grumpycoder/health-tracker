using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// Tries the primary LLM (Gemini) and, if it errors (e.g. Google returns 503 "overloaded") and a
/// fallback key is configured (Groq), retries the same prompt on the fallback. Gemini stays primary
/// for label/plate accuracy; the fallback only prevents "AI unavailable" when the primary is down.
/// </summary>
public sealed class FallbackLlmClient : ILlmClient
{
    private readonly GeminiLlmClient _primary;
    private readonly GroqLlmClient _fallback;

    public FallbackLlmClient(GeminiLlmClient primary, GroqLlmClient fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    // AI is offered whenever EITHER provider has a key (so a Groq-only setup still works).
    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        await _primary.IsConfiguredAsync(ct) || await _fallback.IsConfiguredAsync(ct);

    public async Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default)
    {
        // If only the fallback is set up, use it directly.
        if (!await _primary.IsConfiguredAsync(ct) && await _fallback.IsConfiguredAsync(ct))
            return await _fallback.GenerateJsonAsync(prompt, imageJpeg, ct);

        try
        {
            return await _primary.GenerateJsonAsync(prompt, imageJpeg, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Primary failed (overloaded / quota / transient) — fall back if a Groq key is set,
            // otherwise surface the original error.
            if (await _fallback.IsConfiguredAsync(ct))
                return await _fallback.GenerateJsonAsync(prompt, imageJpeg, ct);
            throw;
        }
    }
}

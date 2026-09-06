using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// Tries the primary LLM and, if it errors, retries the same prompt on the fallback. Mistral is
/// primary here (Gemini's free tier is overloaded most of the time for this user); Gemini is the
/// fallback (better label accuracy when it's actually available). The fallback only kicks in when
/// the primary is down or its key isn't set.
/// </summary>
public sealed class FallbackLlmClient : ILlmClient
{
    private readonly MistralLlmClient _primary;
    private readonly GeminiLlmClient _fallback;

    public FallbackLlmClient(MistralLlmClient primary, GeminiLlmClient fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    // AI is offered whenever EITHER provider has a key (so a single-provider setup still works).
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
            // Primary failed — fall back to the other provider if its key is set, else rethrow.
            if (await _fallback.IsConfiguredAsync(ct))
                return await _fallback.GenerateJsonAsync(prompt, imageJpeg, ct);
            throw;
        }
    }
}

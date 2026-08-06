namespace FitRecoveryLog.Application.Ai;

/// <summary>
/// Transport port for a large-language-model provider — the single seam that makes the AI
/// features provider-agnostic. Swapping Gemini for another LLM (OpenAI, Claude, a local model)
/// is a new adapter plus one DI registration; the prompts and response parsing are unchanged.
/// Implementations send the prompt in JSON-response mode and return the model's raw text
/// (callers parse it into their own DTOs).
/// </summary>
public interface ILlmClient
{
    /// <summary>Whether the provider is ready to use (e.g. the phone has an API key; a server
    /// proxy is always ready). Drives whether the UI offers AI features.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>Send a prompt — optionally with a JPEG image for vision — and return the model's
    /// JSON text response. Throws <see cref="InvalidOperationException"/> when the provider isn't
    /// configured (no key) or returns an error.</summary>
    Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default);
}

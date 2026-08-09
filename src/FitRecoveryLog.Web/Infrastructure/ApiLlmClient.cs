using System.Net.Http.Json;
using System.Text.Json;
using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>
/// Web <see cref="ILlmClient"/>: the browser never holds the provider key, so it proxies prompts
/// through the backend (<c>/api/v1/ai/generate</c>, server-side key) on the same authenticated
/// HttpClient the sync API uses. Identical contract to the phone's GeminiLlmClient — different
/// transport, so the AI features run unchanged on both clients.
/// </summary>
public sealed class ApiLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    public ApiLlmClient(HttpClient http) => _http = http;

    // The server proxy owns the key, so the web is always "configured" — availability is a
    // server concern (a missing key surfaces as an error on the actual call).
    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);

    public async Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default)
    {
        var payload = new
        {
            prompt,
            imageJpegBase64 = imageJpeg is null ? null : Convert.ToBase64String(imageJpeg),
        };

        using var resp = await _http.PostAsJsonAsync("/api/v1/ai/generate", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            // Prefer the server's { "error": ... } message over a bare status code.
            try
            {
                using var err = JsonDocument.Parse(body);
                if (err.RootElement.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } msg)
                    throw new InvalidOperationException(msg);
            }
            catch (JsonException) { }
            throw new InvalidOperationException($"AI request failed ({(int)resp.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
    }
}

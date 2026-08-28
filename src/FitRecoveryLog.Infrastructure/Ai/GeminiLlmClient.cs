using System.Text;
using System.Text.Json;
using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// Gemini implementation of <see cref="ILlmClient"/>: a JSON-mode <c>generateContent</c> call,
/// with the user's key pulled from <see cref="ILlmKeyStore"/> (never passed around by callers).
/// This is the only Gemini-specific code — swapping providers means a sibling adapter, nothing else.
/// </summary>
public sealed class GeminiLlmClient : ILlmClient
{
    private const string Model = "gemini-2.5-flash";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly ILlmKeyStore _keys;
    public GeminiLlmClient(ILlmKeyStore keys) => _keys = keys;

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(await _keys.GetAsync());

    public async Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default)
    {
        var apiKey = await _keys.GetAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key set (Settings).");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";
        object parts = imageJpeg is null
            ? new object[] { new { text = prompt } }
            : new object[]
            {
                new { text = prompt },
                new { inline_data = new { mime_type = "image/jpeg", data = Convert.ToBase64String(imageJpeg) } }
            };
        var payload = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts } },
            generationConfig = new { response_mime_type = "application/json" }
        });

        // Gemini's free tier returns 429/500/503 when overloaded — retry a couple times with
        // backoff before giving up (the FallbackLlmClient then tries the secondary provider).
        HttpResponseMessage resp = null!;
        string body = "";
        for (var attempt = 0; ; attempt++)
        {
            using var attemptReq = new HttpRequestMessage(HttpMethod.Post, url);
            attemptReq.Headers.Add("x-goog-api-key", apiKey);
            attemptReq.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            resp = await Http.SendAsync(attemptReq, ct);
            body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode || attempt >= 2 || (int)resp.StatusCode is not (429 or 500 or 503))
                break;
            resp.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(700 * (attempt + 1)), ct);
        }
        using var _ = resp;
        if (!resp.IsSuccessStatusCode)
        {
            // Surface Gemini's error message (bad key, quota, etc.) without the JSON noise.
            try
            {
                using var err = JsonDocument.Parse(body);
                var message = err.RootElement.GetProperty("error").GetProperty("message").GetString();
                if (!string.IsNullOrWhiteSpace(message))
                    throw new InvalidOperationException(message);
            }
            catch (KeyNotFoundException) { }
            catch (JsonException) { }
            throw new InvalidOperationException($"Gemini returned {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
    }
}

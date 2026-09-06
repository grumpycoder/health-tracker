using System.Text;
using System.Text.Json;
using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// Mistral (La Plateforme) implementation of <see cref="ILlmClient"/> — OpenAI-compatible
/// chat/completions with the Pixtral vision model. Free tier, used as an automatic fallback when
/// Gemini is overloaded. Key stored separately (<c>ILlmKeyStore.GetAsync("mistral")</c>).
/// </summary>
public sealed class MistralLlmClient : ILlmClient
{
    // Pixtral does vision + JSON mode and is on the free tier. Update here if Mistral renames it
    // (see https://docs.mistral.ai/getting-started/models/).
    private const string Model = "pixtral-12b-2409";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly ILlmKeyStore _keys;
    public MistralLlmClient(ILlmKeyStore keys) => _keys = keys;

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(await _keys.GetAsync("mistral"));

    public async Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default)
    {
        var apiKey = await _keys.GetAsync("mistral");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Mistral API key set (Settings).");

        // OpenAI content: a plain string for text-only, or a parts array when an image is attached.
        object content = imageJpeg is null
            ? prompt
            : new object[]
            {
                new { type = "text", text = prompt },
                new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(imageJpeg)}" } }
            };
        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            messages = new object[] { new { role = "user", content } },
            response_format = new { type = "json_object" },
            temperature = 0.2
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            try
            {
                using var err = JsonDocument.Parse(body);
                var message = err.RootElement.GetProperty("error").GetProperty("message").GetString();
                if (!string.IsNullOrWhiteSpace(message))
                    throw new InvalidOperationException(message);
            }
            catch (KeyNotFoundException) { }
            catch (JsonException) { }
            throw new InvalidOperationException($"Mistral returned {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var content2 = doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
        return ExtractJsonObject(content2);
    }

    // Pixtral may wrap the JSON in ```json fences or add stray prose — return just the JSON object.
    private static string? ExtractJsonObject(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return start >= 0 && end > start ? s.Substring(start, end - start + 1) : s;
    }
}

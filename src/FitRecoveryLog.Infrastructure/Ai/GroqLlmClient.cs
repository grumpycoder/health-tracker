using System.Text;
using System.Text.Json;
using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// Groq implementation of <see cref="ILlmClient"/> (OpenAI-compatible chat/completions). Used as an
/// automatic fallback when Gemini is overloaded. Vision-capable, JSON-response mode. The Groq key is
/// stored separately (<c>ILlmKeyStore.GetAsync("groq")</c>).
/// </summary>
public sealed class GroqLlmClient : ILlmClient
{
    // Groq's free multimodal (vision) model. If Groq deprecates it, update here — see the model
    // list at https://console.groq.com/docs/models.
    private const string Model = "meta-llama/llama-4-scout-17b-16e-instruct";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly ILlmKeyStore _keys;
    public GroqLlmClient(ILlmKeyStore keys) => _keys = keys;

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(await _keys.GetAsync("groq"));

    public async Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default)
    {
        var apiKey = await _keys.GetAsync("groq");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Groq API key set (Settings).");

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

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
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
            throw new InvalidOperationException($"Groq returned {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
    }
}

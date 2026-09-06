using System.Text;
using System.Text.Json;
using FitRecoveryLog.Application.Ai;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// GitHub Models implementation of <see cref="ILlmClient"/> (Azure AI inference, OpenAI-compatible
/// chat/completions). Free, rate-limited, vision-capable — used as an automatic fallback when Gemini
/// is overloaded. Auth is a GitHub personal-access-token with <c>models:read</c>, stored separately
/// (<c>ILlmKeyStore.GetAsync("github")</c>).
/// </summary>
public sealed class GitHubModelsLlmClient : ILlmClient
{
    // GPT-4o-mini does vision + JSON mode. Namespaced model id for the GitHub Models GA endpoint.
    private const string Model = "openai/gpt-4o-mini";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly ILlmKeyStore _keys;
    public GitHubModelsLlmClient(ILlmKeyStore keys) => _keys = keys;

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(await _keys.GetAsync("github"));

    public async Task<string?> GenerateJsonAsync(string prompt, byte[]? imageJpeg = null, CancellationToken ct = default)
    {
        var token = await _keys.GetAsync("github");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("No GitHub token set (Settings).");

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

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://models.github.ai/inference/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {token}");
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
            throw new InvalidOperationException($"GitHub Models returned {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
    }
}

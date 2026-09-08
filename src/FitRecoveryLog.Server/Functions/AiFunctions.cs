using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FitRecoveryLog.Server.Functions;

/// <summary>Prompt (and optional JPEG for vision) sent by the web ILlmClient.</summary>
public sealed record AiGenerateRequest(string? Prompt, string? ImageJpegBase64);

/// <summary>
/// Server-side LLM proxy (<c>/api/v1/ai/generate</c>) so the browser never holds a provider key.
/// Auth is enforced up front by <see cref="Auth.AuthMiddleware"/>. Mirrors the phone: tries
/// <b>Mistral (Pixtral) first, then falls back to Gemini</b> — keys come from the
/// <c>MistralApiKey</c> and <c>GeminiApiKey</c> app settings (either alone is enough). Sends the
/// prompt in JSON-response mode and returns the model's raw text as <c>{ "text": ... }</c> (the
/// caller parses it into its own DTO).
/// </summary>
public sealed class AiFunctions
{
    private const string GeminiModel = "gemini-2.5-flash";
    private const string MistralModel = "pixtral-12b-2409";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("AiGenerate")]
    public async Task<IActionResult> Generate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/ai/generate")] HttpRequest req)
    {
        var mistralKey = Environment.GetEnvironmentVariable("MistralApiKey");
        var geminiKey = Environment.GetEnvironmentVariable("GeminiApiKey");
        if (string.IsNullOrWhiteSpace(mistralKey) && string.IsNullOrWhiteSpace(geminiKey))
            return new ObjectResult(new { error = "AI is not configured on the server." })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };

        using var reader = new StreamReader(req.Body);
        var raw = await reader.ReadToEndAsync();
        AiGenerateRequest body;
        try { body = JsonSerializer.Deserialize<AiGenerateRequest>(raw, Json) ?? new(null, null); }
        catch (JsonException) { return new BadRequestObjectResult(new { error = "invalid JSON body" }); }

        if (string.IsNullOrWhiteSpace(body.Prompt))
            return new BadRequestObjectResult(new { error = "prompt is required" });

        byte[]? image = null;
        if (!string.IsNullOrWhiteSpace(body.ImageJpegBase64))
        {
            try { image = Convert.FromBase64String(body.ImageJpegBase64); }
            catch (FormatException) { return new BadRequestObjectResult(new { error = "imageJpegBase64 is not valid base64" }); }
        }

        var ct = req.HttpContext.RequestAborted;
        // Provider order mirrors the phone: Mistral primary, Gemini fallback. Try each configured
        // provider in turn; only the last failure surfaces if they all fail.
        InvalidOperationException? lastError = null;
        foreach (var provider in Providers(mistralKey, geminiKey))
        {
            try
            {
                var text = await provider(body.Prompt!, image, ct);
                return new OkObjectResult(new { text });
            }
            catch (OperationCanceledException) { throw; }        // client hung up — don't fall over
            catch (InvalidOperationException ex) { lastError = ex; } // bad key / quota / load → next provider
        }
        // Surface the provider's message (bad key, quota) as a bad-gateway, not a 500.
        return new ObjectResult(new { error = lastError?.Message ?? "All AI providers failed." })
        { StatusCode = StatusCodes.Status502BadGateway };
    }

    private delegate Task<string?> ProviderCall(string prompt, byte[]? imageJpeg, CancellationToken ct);

    private static IEnumerable<ProviderCall> Providers(string? mistralKey, string? geminiKey)
    {
        if (!string.IsNullOrWhiteSpace(mistralKey))
            yield return (p, img, ct) => CallMistralAsync(mistralKey!, p, img, ct);
        if (!string.IsNullOrWhiteSpace(geminiKey))
            yield return (p, img, ct) => CallGeminiAsync(geminiKey!, p, img, ct);
    }

    // Answers the browser's CORS preflight (sets headers itself + returns a body — a bodyless
    // 204 drops headers in the isolated ASP.NET model, same as the sync preflight).
    [Function("AiGeneratePreflight")]
    public IActionResult Preflight(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "v1/ai/generate")] HttpRequest req)
    {
        Auth.CorsMiddleware.ApplyHeaders(req.HttpContext);
        return new OkObjectResult(new { ok = true });
    }

    // OpenAI-compatible chat/completions. Image goes in as a data-URI image_url part; json_object
    // response format keeps the reply parseable. Matches the phone's MistralLlmClient.
    private static async Task<string?> CallMistralAsync(string apiKey, string prompt, byte[]? imageJpeg, CancellationToken ct)
    {
        object content = imageJpeg is null
            ? prompt
            : new object[]
            {
                new { type = "text", text = prompt },
                new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(imageJpeg)}" } }
            };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = MistralModel,
            messages = new[] { new { role = "user", content } },
            response_format = new { type = "json_object" }
        }), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractError(respBody) ?? $"Mistral returned {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(respBody);
        var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return ExtractJsonObject(text);
    }

    private static async Task<string?> CallGeminiAsync(string apiKey, string prompt, byte[]? imageJpeg, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("x-goog-api-key", apiKey);
        object parts = imageJpeg is null
            ? new object[] { new { text = prompt } }
            : new object[]
            {
                new { text = prompt },
                new { inline_data = new { mime_type = "image/jpeg", data = Convert.ToBase64String(imageJpeg) } }
            };
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts } },
            generationConfig = new { response_mime_type = "application/json" }
        }), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractError(respBody) ?? $"Gemini returned {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(respBody);
        return doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
    }

    // Pull a provider's error.message out of its JSON error body, if present.
    private static string? ExtractError(string respBody)
    {
        try
        {
            using var err = JsonDocument.Parse(respBody);
            if (err.RootElement.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m))
                    return m.GetString();
                if (e.ValueKind == JsonValueKind.String) return e.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    // Pixtral can wrap the JSON in prose or fences; keep only the outermost { … } object.
    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text;
    }
}

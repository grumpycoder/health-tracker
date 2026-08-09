using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FitRecoveryLog.Server.Functions;

/// <summary>Prompt (and optional JPEG for vision) sent by the web ILlmClient.</summary>
public sealed record AiGenerateRequest(string? Prompt, string? ImageJpegBase64);

/// <summary>
/// Server-side LLM proxy (<c>/api/v1/ai/generate</c>) so the browser never holds the provider
/// key. Auth is enforced up front by <see cref="Auth.AuthMiddleware"/>; the Gemini key comes from
/// the <c>GeminiApiKey</c> app setting. Sends the prompt in JSON-response mode and returns the
/// model's raw text as <c>{ "text": ... }</c> (the caller parses it into its own DTO). Swapping
/// providers is a change to this one function — the client's <c>ILlmClient</c> is unaffected.
/// </summary>
public sealed class AiFunctions
{
    private const string Model = "gemini-2.5-flash";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("AiGenerate")]
    public async Task<IActionResult> Generate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/ai/generate")] HttpRequest req)
    {
        var apiKey = Environment.GetEnvironmentVariable("GeminiApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
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

        try
        {
            var text = await CallGeminiAsync(apiKey, body.Prompt!, image, req.HttpContext.RequestAborted);
            return new OkObjectResult(new { text });
        }
        catch (InvalidOperationException ex)
        {
            // Surface the provider's message (bad key, quota) as a bad-gateway, not a 500.
            return new ObjectResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status502BadGateway };
        }
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

    private static async Task<string?> CallGeminiAsync(string apiKey, string prompt, byte[]? imageJpeg, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";
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
        {
            try
            {
                using var err = JsonDocument.Parse(respBody);
                var message = err.RootElement.GetProperty("error").GetProperty("message").GetString();
                if (!string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException(message);
            }
            catch (KeyNotFoundException) { }
            catch (JsonException) { }
            throw new InvalidOperationException($"Gemini returned {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(respBody);
        return doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
    }
}

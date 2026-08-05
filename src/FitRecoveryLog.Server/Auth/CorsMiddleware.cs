using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace FitRecoveryLog.Server.Auth;

/// <summary>
/// CORS for browser clients (the Blazor web app). Allowed origins come from the
/// <c>AllowedOrigins</c> app setting (comma-separated); an origin is echoed back only if it
/// is on the list. The ASP.NET Core integration model owns the HTTP pipeline, so platform
/// CORS doesn't apply — we set the headers here. The header logic is a static helper so the
/// OPTIONS preflight function can also apply it (a bodyless 204 short-circuit drops
/// middleware-set headers, so the preflight sets them itself and returns a body).
/// </summary>
public sealed class CorsMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly HashSet<string> Allowed =
        (Environment.GetEnvironmentVariable("AllowedOrigins") ?? "https://localhost:5002")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets CORS response headers when the request's Origin is allowed.</summary>
    public static void ApplyHeaders(HttpContext http)
    {
        var origin = http.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || !Allowed.Contains(origin)) return;

        var h = http.Response.Headers;
        h["Access-Control-Allow-Origin"] = origin;
        h["Access-Control-Allow-Headers"] = "authorization,content-type";
        h["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
        h["Access-Control-Max-Age"] = "3600";
        h["Vary"] = "Origin";
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = context.GetHttpContext();
        if (http is not null) ApplyHeaders(http);
        await next(context);
    }
}

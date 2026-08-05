using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace FitRecoveryLog.Server.Auth;

/// <summary>
/// Rejects any HTTP-triggered request without a valid bearer token before the function
/// body runs. The health ping (<c>/api/v1/ping</c>) is exempt so uptime checks don't need
/// a token. Non-HTTP triggers pass through untouched.
/// </summary>
public sealed class AuthMiddleware : IFunctionsWorkerMiddleware
{
    private readonly TokenValidator _validator;

    public AuthMiddleware(TokenValidator validator) => _validator = validator;

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = context.GetHttpContext();
        if (http is null)
        {
            await next(context); // not an HTTP trigger
            return;
        }

        // CORS preflight carries no token; let OPTIONS through (the SyncPreflight function
        // answers it with CORS headers). Also exempt the health ping.
        var path = http.Request.Path.Value ?? "";
        if (HttpMethods.IsOptions(http.Request.Method) || path.EndsWith("/ping", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var authHeader = http.Request.Headers.Authorization.ToString();
        if (!await _validator.IsValidAsync(authHeader, http.RequestAborted))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await http.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return; // short-circuit — do not call next
        }

        await next(context);
    }
}

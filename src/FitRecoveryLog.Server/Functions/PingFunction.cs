using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FitRecoveryLog.Server.Functions;

/// <summary>Unauthenticated liveness check (exempted in <see cref="Auth.AuthMiddleware"/>).</summary>
public sealed class PingFunction
{
    [Function("Ping")]
    public IActionResult Ping(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/ping")] HttpRequest req)
        => new OkObjectResult(new { status = "ok", utc = DateTime.UtcNow });
}

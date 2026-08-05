using System.Text.Json;
using FitRecoveryLog.Server.Sync;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FitRecoveryLog.Server.Functions;

/// <summary>
/// The versioned sync API (<c>/api/v1/sync</c>). GET pulls everything newer than the
/// client's cursor; POST pushes the client's locally-changed rows. Auth is enforced up
/// front by <see cref="Auth.AuthMiddleware"/>, so these run as trusted.
/// </summary>
public sealed class SyncFunctions
{
    private readonly CloudDbContext _db;
    private readonly SyncEngine _engine = new();

    public SyncFunctions(CloudDbContext db) => _db = db;

    [Function("SyncPull")]
    public async Task<IActionResult> Pull(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sync")] HttpRequest req)
    {
        long since = 0;
        if (long.TryParse(req.Query["since"], out var s) && s > 0) since = s;

        var resp = await _engine.PullAsync(_db, since);
        return new OkObjectResult(resp);
    }

    [Function("SyncPush")]
    public async Task<IActionResult> Push(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/sync")] HttpRequest req)
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();

        SyncPushRequest push;
        try
        {
            push = JsonSerializer.Deserialize<SyncPushRequest>(body, SyncEngine.JsonOpts) ?? new();
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "invalid JSON body" });
        }

        var applied = await _engine.PushAsync(_db, push);
        // No cursor query here: the client advances its own cursors from local state and the
        // pull response, and re-pulling just-pushed rows is idempotent. Computing a server
        // cursor meant ~40 extra queries per push — costly on a cold serverless DB.
        return new OkObjectResult(new SyncPushResponse { Applied = applied, Cursor = 0 });
    }

    // Answers the browser's CORS preflight. Sets the CORS headers itself and returns a
    // body-bearing 200 — a bodyless 204 drops headers in the isolated ASP.NET model.
    [Function("SyncPreflight")]
    public IActionResult Preflight(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "v1/sync")] HttpRequest req)
    {
        Auth.CorsMiddleware.ApplyHeaders(req.HttpContext);
        return new OkObjectResult(new { ok = true });
    }
}

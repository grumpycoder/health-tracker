namespace FitRecoveryLog;

/// <summary>Bridges notification taps (MAUI layer) to Blazor navigation.
/// Cold start: the route is stashed until the layout initializes and consumes it.
/// Warm: the layout listens for RouteRequested and navigates immediately.</summary>
public static class NotificationNav
{
    private static string? _pendingRoute;

    public static event Action<string>? RouteRequested;

    public static void Go(string route)
    {
        _pendingRoute = route;
        RouteRequested?.Invoke(route);
    }

    /// <summary>Returns and clears the stashed route, if any.</summary>
    public static string? Consume()
    {
        var r = _pendingRoute;
        _pendingRoute = null;
        return r;
    }
}

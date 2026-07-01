namespace FitRecoveryLog.Services;

/// <summary>
/// App-wide transient toast. Rendered by MainLayout outside the scrolling
/// content area, so the fixed-position banner pins to the viewport reliably
/// (a WKWebView fixed-inside-scroll-container quirk otherwise hides it).
/// </summary>
public sealed class ToastService
{
    public string? Message { get; private set; }
    public event Func<Task>? OnChange;
    private int _token;

    public async Task Show(string message, int milliseconds = 2500)
    {
        var token = ++_token;
        Message = message;
        await Notify();
        await Task.Delay(milliseconds);
        if (token == _token) { Message = null; await Notify(); }
    }

    private Task Notify() => OnChange?.Invoke() ?? Task.CompletedTask;
}

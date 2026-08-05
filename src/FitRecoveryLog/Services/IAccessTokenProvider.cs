namespace FitRecoveryLog.Services;

/// <summary>
/// Supplies bearer tokens for the sync API. Abstracted from MSAL so the sync engine is
/// testable without a real identity provider.
/// </summary>
public interface IAccessTokenProvider
{
    bool IsSignedIn { get; }
    string? Username { get; }

    /// <summary>Returns a valid access token, or null if none is available. When
    /// <paramref name="allowInteractive"/> is true, may pop the sign-in UI; when false,
    /// only a silent (cached/refreshed) token is attempted.</summary>
    Task<string?> GetTokenAsync(bool allowInteractive, CancellationToken ct = default);

    Task SignOutAsync();

    /// <summary>Raised when sign-in state changes so the UI can refresh.</summary>
    event Action? StateChanged;
}

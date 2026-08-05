using Microsoft.Identity.Client;

namespace FitRecoveryLog.Services;

/// <summary>
/// MSAL-backed token provider. Signs in against the personal Microsoft tenant and caches
/// tokens in the platform keychain (MSAL's default cache on iOS/MacCatalyst), so after the
/// first interactive sign-in tokens refresh silently.
/// </summary>
public sealed class MsalAuthService : IAccessTokenProvider
{
    private readonly IPublicClientApplication _pca;
    private IAccount? _account;

    public event Action? StateChanged;

    public MsalAuthService()
    {
        var builder = PublicClientApplicationBuilder
            .Create(SyncConfig.ClientId)
            .WithAuthority(SyncConfig.Authority)
            .WithRedirectUri(SyncConfig.RedirectUri);

#if IOS || MACCATALYST
        // Keeps the MSAL token cache in this app's own keychain group (no shared
        // com.microsoft.adalcache group needed). Must match a keychain-access-groups
        // entitlement on device builds.
        builder = builder.WithIosKeychainSecurityGroup("com.mlawrence.fitrecoverylog");
#endif
        _pca = builder.Build();
    }

    public bool IsSignedIn => _account is not null;
    public string? Username => _account?.Username;

    public async Task<string?> GetTokenAsync(bool allowInteractive, CancellationToken ct = default)
    {
        _account ??= (await _pca.GetAccountsAsync()).FirstOrDefault();

        // 1) Silent first — uses the cached/refreshed token when possible.
        if (_account is not null)
        {
            try
            {
                var silent = await _pca.AcquireTokenSilent(SyncConfig.Scopes, _account)
                    .ExecuteAsync(ct);
                NotifyIfAccountChanged(silent.Account);
                return silent.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                // fall through to interactive
            }
        }

        if (!allowInteractive) return null;

        // 2) Interactive sign-in.
        try
        {
            var interactive = await _pca.AcquireTokenInteractive(SyncConfig.Scopes)
                .WithUseEmbeddedWebView(false) // system web view (ASWebAuthenticationSession)
                .ExecuteAsync(ct);
            NotifyIfAccountChanged(interactive.Account);
            return interactive.AccessToken;
        }
        catch (MsalClientException)
        {
            // User cancelled or the platform web view was dismissed.
            return null;
        }
    }

    public async Task SignOutAsync()
    {
        foreach (var acct in await _pca.GetAccountsAsync())
            await _pca.RemoveAsync(acct);
        _account = null;
        StateChanged?.Invoke();
    }

    private void NotifyIfAccountChanged(IAccount? account)
    {
        var was = _account?.HomeAccountId?.Identifier;
        _account = account;
        if (was != account?.HomeAccountId?.Identifier)
            StateChanged?.Invoke();
    }
}

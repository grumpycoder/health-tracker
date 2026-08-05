using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace FitRecoveryLog.Web;

/// <summary>
/// Attaches the MSAL access token (for the sync API's scope) to requests bound for the
/// cloud API's origin. The default BaseAddressAuthorizationMessageHandler only authorizes
/// the app's own origin, so a cross-origin API needs this explicit ConfigureHandler.
/// </summary>
public sealed class ApiAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public ApiAuthorizationMessageHandler(IAccessTokenProvider provider, NavigationManager navigation)
        : base(provider, navigation)
    {
        ConfigureHandler(
            authorizedUrls: new[] { SyncApi.BaseUrl },
            scopes: new[] { SyncApi.Scope });
    }
}

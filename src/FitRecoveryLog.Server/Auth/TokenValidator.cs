using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace FitRecoveryLog.Server.Auth;

/// <summary>
/// Validates incoming JWT bearer tokens against a personal Microsoft identity (NOT the
/// company Entra tenant). Signing keys/issuer are discovered from the authority's OIDC
/// metadata and cached/rotated by <see cref="ConfigurationManager{T}"/>.
///
/// Single-user lockdown: if <c>AuthAllowedUserId</c> is set, only that user's token
/// (matched on the <c>oid</c>/<c>sub</c> claim) is accepted — so even a validly-issued
/// token for someone else is rejected.
///
/// Config (app settings / local.settings.json):
///   AuthAuthority       e.g. https://login.microsoftonline.com/consumers/v2.0
///   AuthAudience        the API app registration's client id (expected 'aud')
///   AuthAllowedUserId   optional: the only oid/sub allowed
///   AuthDevBypass       "true" to skip validation for local dev only
/// </summary>
public sealed class TokenValidator
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private readonly string _audience;
    private readonly string? _allowedUserId;
    private readonly bool _devBypass;

    public TokenValidator()
    {
        _devBypass = string.Equals(
            Environment.GetEnvironmentVariable("AuthDevBypass"), "true", StringComparison.OrdinalIgnoreCase);
        _audience = Environment.GetEnvironmentVariable("AuthAudience") ?? "";
        _allowedUserId = Environment.GetEnvironmentVariable("AuthAllowedUserId");

        var authority = Environment.GetEnvironmentVariable("AuthAuthority");
        if (!_devBypass && !string.IsNullOrWhiteSpace(authority))
        {
            var metadata = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadata, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever());
        }
    }

    public async Task<bool> IsValidAsync(string? authorizationHeader, CancellationToken ct = default)
    {
        if (_devBypass) return true;
        if (_configManager is null) return false; // misconfigured — fail closed

        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authorizationHeader["Bearer ".Length..].Trim();

        OpenIdConnectConfiguration config;
        try { config = await _configManager.GetConfigurationAsync(ct); }
        catch { return false; }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config.Issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid) return false;

        if (!string.IsNullOrEmpty(_allowedUserId))
        {
            var oid = ClaimValue(result, "oid") ?? ClaimValue(result, "sub");
            if (!string.Equals(oid, _allowedUserId, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string? ClaimValue(TokenValidationResult result, string type) =>
        result.Claims.TryGetValue(type, out var v) ? v?.ToString() : null;
}

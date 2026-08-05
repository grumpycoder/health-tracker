namespace FitRecoveryLog.Services;

/// <summary>
/// Cloud sync endpoints and identity coordinates. None of these are secrets — they're
/// public client/tenant identifiers and a hostname. The SQL password and tokens never
/// live in the app. See docs/sync-architecture.md and infra/ for the backend.
/// </summary>
public static class SyncConfig
{
    public const string ApiBaseUrl = "https://fitlog-api-b6c3yia2b5u3g.azurewebsites.net";

    public const string TenantId = "33e233f9-9d88-4315-8c8c-7e6f30e2bcb0";
    public const string Authority = $"https://login.microsoftonline.com/{TenantId}";

    /// <summary>Public (mobile) app registration this app signs in as.</summary>
    public const string ClientId = "e44c3f97-9a65-46d5-b50d-e26c677fdf92";

    /// <summary>API app registration; the token audience.</summary>
    public const string ApiClientId = "3688a639-d636-4e0c-94be-cab645dd5927";

    /// <summary>Delegated scope exposed by the API.</summary>
    public const string Scope = $"api://{ApiClientId}/access_as_user";

    /// <summary>Matches the redirect URI registered on the mobile app + Info.plist scheme.</summary>
    public const string RedirectUri = "msauth.com.mlawrence.fitrecoverylog://auth";

    public static readonly string[] Scopes = { Scope };
}

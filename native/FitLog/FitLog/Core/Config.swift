import Foundation

/// Same public identifiers as the MAUI app's SyncConfig (none are secrets).
enum Config {
    static let apiBase = URL(string: "https://fitlog-api-b6c3yia2b5u3g.azurewebsites.net")!
    static let tenant = "33e233f9-9d88-4315-8c8c-7e6f30e2bcb0"
    static let clientId = "e44c3f97-9a65-46d5-b50d-e26c677fdf92"
    static let scope = "api://3688a639-d636-4e0c-94be-cab645dd5927/access_as_user"
    // Reuses the mobile app's registered redirect. ASWebAuthenticationSession captures the
    // callback itself, so this app doesn't need to own the URL scheme (and doesn't fight the
    // MAUI app for it).
    static let redirectUri = "msauth.com.mlawrence.fitrecoverylog://auth"
    static let callbackScheme = "msauth.com.mlawrence.fitrecoverylog"
}

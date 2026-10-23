import SwiftUI
import AuthenticationServices
import CryptoKit
import Foundation
import Security

/// Entra ID sign-in: OAuth auth-code + PKCE in Apple's web-auth sheet, refresh token in the Keychain.
@MainActor
final class Auth: ObservableObject {
    @Published private(set) var isSignedIn = false
    private var accessToken: String?
    private var expiresAt = Date.distantPast

    private var authority: String { "https://login.microsoftonline.com/\(Config.tenant)/oauth2/v2.0" }
    private var scopes: String { "\(Config.scope) offline_access openid profile" }

    init() { isSignedIn = Keychain.read("refresh") != nil }

    func signIn(using session: WebAuthenticationSession) async throws {
        let verifier = Self.randomURLSafe(64)
        let challenge = Data(SHA256.hash(data: Data(verifier.utf8))).base64URL
        var c = URLComponents(string: "\(authority)/authorize")!
        c.queryItems = [
            .init(name: "client_id", value: Config.clientId),
            .init(name: "response_type", value: "code"),
            .init(name: "redirect_uri", value: Config.redirectUri),
            .init(name: "response_mode", value: "query"),
            .init(name: "scope", value: scopes),
            .init(name: "code_challenge", value: challenge),
            .init(name: "code_challenge_method", value: "S256"),
            .init(name: "prompt", value: "select_account"),
        ]
        let callback = try await session.authenticate(using: c.url!, callbackURLScheme: Config.callbackScheme,
                                                      preferredBrowserSession: .shared)
        guard let code = URLComponents(url: callback, resolvingAgainstBaseURL: false)?
            .queryItems?.first(where: { $0.name == "code" })?.value else { throw AuthError.noCode }
        try await redeem(["grant_type": "authorization_code", "code": code,
                          "redirect_uri": Config.redirectUri, "code_verifier": verifier])
    }

    func signOut() {
        Keychain.delete("refresh")
        accessToken = nil
        isSignedIn = false
    }

    /// A valid access token, refreshing silently when needed.
    func token() async throws -> String {
        if let t = accessToken, expiresAt > Date().addingTimeInterval(60) { return t }
        guard let refresh = Keychain.read("refresh") else { isSignedIn = false; throw AuthError.signedOut }
        try await redeem(["grant_type": "refresh_token", "refresh_token": refresh])
        return accessToken!
    }

    private func redeem(_ params: [String: String]) async throws {
        var req = URLRequest(url: URL(string: "\(authority)/token")!)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        let all = params.merging(["client_id": Config.clientId, "scope": scopes]) { a, _ in a }
        req.httpBody = all.map { "\($0.key)=\($0.value.formEncoded)" }.joined(separator: "&").data(using: .utf8)
        let (data, resp) = try await URLSession.shared.data(for: req)
        struct TokenResponse: Decodable { let access_token: String; let refresh_token: String?; let expires_in: Int }
        let status = (resp as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200, let t = try? JSONDecoder().decode(TokenResponse.self, from: data) else {
            // Only a rejected grant (expired/revoked refresh token) means signed out; a 5xx or
            // throttle is temporary — keep the refresh token and let the next attempt retry.
            if params["grant_type"] == "refresh_token", status == 400 || status == 401 { signOut() }
            throw AuthError.tokenFailed(String(data: data, encoding: .utf8) ?? "")
        }
        accessToken = t.access_token
        expiresAt = Date().addingTimeInterval(TimeInterval(t.expires_in))
        if let r = t.refresh_token { Keychain.write("refresh", r) }
        isSignedIn = true
    }

    private static func randomURLSafe(_ n: Int) -> String {
        var bytes = [UInt8](repeating: 0, count: n)
        _ = SecRandomCopyBytes(kSecRandomDefault, n, &bytes)
        return Data(bytes).base64URL
    }
}

enum AuthError: LocalizedError {
    case noCode, signedOut, tokenFailed(String)
    var errorDescription: String? {
        switch self {
        case .noCode: "Sign-in didn't return a code."
        case .signedOut: "Signed out — sign in again."
        case .tokenFailed(let body): "Token request failed. \(body.prefix(200))"
        }
    }
}

enum Keychain {
    private static func query(_ key: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "fitlog.native", kSecAttrAccount as String: key]
    }
    static func read(_ key: String) -> String? {
        var q = query(key); q[kSecReturnData as String] = true
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let d = out as? Data else { return nil }
        return String(data: d, encoding: .utf8)
    }
    static func write(_ key: String, _ value: String) {
        delete(key)
        var q = query(key)
        q[kSecValueData as String] = Data(value.utf8)
        q[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        SecItemAdd(q as CFDictionary, nil)
    }
    static func delete(_ key: String) { SecItemDelete(query(key) as CFDictionary) }
}

private extension Data {
    var base64URL: String {
        base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }
}

private extension String {
    var formEncoded: String {
        var allowed = CharacterSet.alphanumerics; allowed.insert(charactersIn: "-._~")
        return addingPercentEncoding(withAllowedCharacters: allowed) ?? self
    }
}

import Foundation

/// Push-then-pull over /api/v1/sync, matching the C# CloudSyncService protocol: push every dirty
/// row (scalar columns, raw server JSON merged with local edits), then pull everything newer than
/// the server cursor (UTC ticks) and apply it.
@MainActor
final class SyncEngine: ObservableObject {
    @Published private(set) var syncing = false
    @Published private(set) var lastSync: Date?
    @Published private(set) var lastResult: String?
    @Published var error: String?

    private let store: RowStore
    private let auth: Auth
    private let session: URLSession
    /// Called after rows changed locally from a pull, so views reload.
    var onChange: (() -> Void)?

    init(store: RowStore, auth: Auth) {
        self.store = store
        self.auth = auth
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest = 180   // serverless SQL may be resuming from pause
        session = URLSession(configuration: cfg)
        lastSync = (try? store.meta("lastSync")).flatMap { $0.flatMap(Double.init) }.map(Date.init(timeIntervalSince1970:))
    }

    func sync() async {
        guard !syncing else { return }
        syncing = true; defer { syncing = false }
        do {
            let token = try await auth.token()
            let pushed = try await push(token)
            let pulled = try await pull(token)
            lastSync = Date()
            try store.setMeta("lastSync", String(lastSync!.timeIntervalSince1970))
            lastResult = "Sent \(pushed), received \(pulled)"
            error = nil
            if pulled > 0 || pushed > 0 { onChange?() }
        } catch {
            self.error = error.localizedDescription
        }
    }

    private func push(_ token: String) async throws -> Int {
        let pending = try store.pending()
        guard !pending.isEmpty else { return 0 }
        var changes: [String: [Row]] = [:]
        for p in pending { changes[p.type, default: []].append(p.row.pushable) }
        var req = request("api/v1/sync", token)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.httpBody = try JSONEncoder().encode(["changes": changes])
        let (data, resp) = try await session.data(for: req)
        try check(resp, data)
        try store.markPushed(pending)
        return pending.count
    }

    private func pull(_ token: String) async throws -> Int {
        let cursor = (try store.meta("pullCursor")).flatMap { $0 } ?? "0"
        let (data, resp) = try await session.data(for: request("api/v1/sync?since=\(cursor)", token))
        try check(resp, data)
        struct Pull: Decodable { let cursor: Int64; let changes: [String: [Row]]; let fullResyncRequired: Bool? }
        let p = try JSONDecoder().decode(Pull.self, from: data)
        if p.fullResyncRequired == true { try store.setMeta("pullCursor", "0"); return 0 }
        let applied = try store.applyPulled(p.changes)
        try store.setMeta("pullCursor", String(p.cursor))
        return applied
    }

    private func request(_ path: String, _ token: String) -> URLRequest {
        var r = URLRequest(url: URL(string: path, relativeTo: Config.apiBase)!)
        r.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        return r
    }

    private func check(_ resp: URLResponse, _ data: Data) throws {
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        guard code == 200 else {
            throw URLError(.badServerResponse, userInfo: [NSLocalizedDescriptionKey:
                "Sync failed (HTTP \(code)) \(String(data: data, encoding: .utf8)?.prefix(160) ?? "")"])
        }
    }
}

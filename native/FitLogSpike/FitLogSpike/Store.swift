import Foundation

/// Read-only data store: pulls everything from /api/v1/sync (never pushes), caches the raw
/// response on disk so the app opens instantly, and exposes typed, live (non-deleted) rows.
@MainActor
final class Store: ObservableObject {
    @Published private(set) var meals: [MealEntry] = []
    @Published private(set) var drinks: [DrinkEntry] = []
    @Published private(set) var logs: [DailyLog] = []
    @Published private(set) var body: [BodyMeasurement] = []
    @Published private(set) var sessions: [WorkoutSession] = []
    @Published private(set) var goals: GoalSettings?
    @Published private(set) var lastSync: Date?
    @Published private(set) var syncing = false
    @Published var error: String?

    private let cacheURL = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("pull.json")

    init() {
        if let data = try? Data(contentsOf: cacheURL) {
            apply(data)
            lastSync = (try? cacheURL.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate
        }
    }

    func refresh(auth: Auth) async {
        guard !syncing else { return }
        syncing = true; defer { syncing = false }
        do {
            var req = URLRequest(url: Config.apiBase.appendingPathComponent("api/v1/sync"))
            req.url = URL(string: req.url!.absoluteString + "?since=0")
            req.timeoutInterval = 180   // serverless SQL may be resuming
            req.setValue("Bearer \(try await auth.token())", forHTTPHeaderField: "Authorization")
            let (data, resp) = try await URLSession.shared.data(for: req)
            guard let http = resp as? HTTPURLResponse, http.statusCode == 200 else {
                throw URLError(.badServerResponse, userInfo: [NSLocalizedDescriptionKey:
                    "Sync failed (HTTP \((resp as? HTTPURLResponse)?.statusCode ?? 0))"])
            }
            apply(data)
            try? data.write(to: cacheURL)
            lastSync = Date()
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }

    private func apply(_ data: Data) {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let changes = root["changes"] as? [String: Any] else { return }
        func rows<T: Decodable>(_ key: String) -> [T] {
            guard let arr = changes[key] as? [Any],
                  let raw = try? JSONSerialization.data(withJSONObject: arr) else { return [] }
            do { return try WireDate.decoder.decode([T].self, from: raw) }
            catch { self.error = "Decode \(key): \(error)"; return [] }
        }
        meals = (rows("MealEntry") as [MealEntry]).filter { !$0.isDeleted }
        drinks = (rows("DrinkEntry") as [DrinkEntry]).filter { !$0.isDeleted }
        logs = (rows("DailyLog") as [DailyLog]).filter { !$0.isDeleted }
        body = (rows("BodyMeasurement") as [BodyMeasurement]).filter { !$0.isDeleted }
        sessions = (rows("WorkoutSession") as [WorkoutSession]).filter { !$0.isDeleted }
        goals = (rows("GoalSettings") as [GoalSettings]).filter { !$0.isDeleted }.max { $0.updatedAt < $1.updatedAt }
    }

    func meals(on day: Day) -> [MealEntry] { meals.filter { Day($0.time) == day }.sorted { $0.time > $1.time } }
    func drinks(on day: Day) -> [DrinkEntry] { drinks.filter { Day($0.time) == day }.sorted { $0.time > $1.time } }
    var energy: EnergyHistory { EnergyHistory(logs: logs, body: body, sessions: sessions) }
}

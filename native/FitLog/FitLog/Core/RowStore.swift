import Foundation
import GRDB

/// Local-first store of synced rows, keyed by (entity type, id), each kept as the server's raw JSON.
/// Typed models are decoded on read; edits merge into the raw row and mark it dirty for the next push.
///
/// Dirty tracking: every local edit bumps `editSeq`. A push clears `dirty` only for rows whose
/// `editSeq` is unchanged since they were collected, so an edit made mid-push isn't lost. Pulled
/// rows never overwrite a dirty row — the local change is pushed next and the server's
/// last-write-wins settles it.
final class RowStore {
    let db: DatabaseQueue

    init(path: String? = nil) throws {
        db = try path.map { try DatabaseQueue(path: $0) } ?? DatabaseQueue()
        var m = DatabaseMigrator()
        m.registerMigration("v1") { db in
            try db.create(table: "syncRow") { t in
                t.column("type", .text).notNull()
                t.column("id", .text).notNull()
                t.column("json", .blob).notNull()
                t.column("dirty", .boolean).notNull().defaults(to: false)
                t.column("editSeq", .integer).notNull().defaults(to: 0)
                t.primaryKey(["type", "id"])
            }
            try db.create(index: "syncRow_dirty", on: "syncRow", columns: ["dirty"])
            try db.create(table: "meta") { t in
                t.primaryKey("key", .text)
                t.column("value", .text)
            }
        }
        try m.migrate(db)
    }

    static func onDisk() throws -> RowStore {
        let dir = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        return try RowStore(path: dir.appendingPathComponent("fitlog.sqlite").path)
    }

    // MARK: reads

    func rows(_ type: String) throws -> [Row] {
        try db.read { db in
            try Data.fetchAll(db, sql: "SELECT json FROM syncRow WHERE type = ?", arguments: [type]).map(Row.decode)
        }
    }

    func row(_ type: String, _ id: String) throws -> Row? {
        try db.read { db in
            try Data.fetchOne(db, sql: "SELECT json FROM syncRow WHERE type = ? AND id = ?", arguments: [type, id.lowercased()])
        }.map(Row.decode)
    }

    /// Live (non-deleted) rows decoded as `T` straight from the stored JSON (one pass — no
    /// detour through `Row`). Rows that fail to decode are skipped, not fatal.
    func decode<T: Decodable>(_ type: String, as: T.Type = T.self) throws -> [T] {
        let blobs = try db.read { db in
            try Data.fetchAll(db, sql: "SELECT json FROM syncRow WHERE type = ?", arguments: [type])
        }
        return blobs.compactMap { data in
            guard let live = try? Wire.decoder.decode(Live<T>.self, from: data), !live.deleted else { return nil }
            return live.value
        }
    }

    // MARK: local edits

    /// Merge `changes` into an existing row (fields not mentioned are kept verbatim).
    func update(_ type: String, _ id: String, _ changes: Row) throws {
        guard var row = try row(type, id) else { throw StoreError.missing(type, id) }
        for (k, v) in changes { row[k] = v }
        try write(type, id, row)
    }

    /// New row: only the fields given plus identity/timestamps. Partial is fine for an insert —
    /// there's no existing server value to lose; C# defaults fill the rest.
    @discardableResult
    func insert(_ type: String, _ fields: Row, now: Date = Date()) throws -> String {
        let id = UUID().uuidString.lowercased()
        var row = fields
        row["id"] = .string(id)
        row["createdAt"] = Wire.utc(now)
        row["updatedAt"] = Wire.utc(now)
        row["isDeleted"] = .bool(false)
        try write(type, id, row)
        return id
    }

    /// Soft delete (tombstone) so the delete reaches the server and other clients.
    func delete(_ type: String, _ id: String, now: Date = Date()) throws {
        try update(type, id, ["isDeleted": .bool(true), "deletedAt": Wire.utc(now)])
    }

    private func write(_ type: String, _ id: String, _ row: Row) throws {
        let data = try row.encoded()
        try db.write { db in
            try db.execute(sql: """
                INSERT INTO syncRow (type, id, json, dirty, editSeq) VALUES (?, ?, ?, 1, 1)
                ON CONFLICT(type, id) DO UPDATE SET json = excluded.json, dirty = 1, editSeq = editSeq + 1
                """, arguments: [type, id.lowercased(), data])
        }
    }

    // MARK: sync plumbing

    struct Pending { let type: String; let id: String; let row: Row; let editSeq: Int64 }

    func pending() throws -> [Pending] {
        try db.read { db in
            try SQLiteRow.fetchAll(db, sql: "SELECT type, id, json, editSeq FROM syncRow WHERE dirty = 1").map {
                Pending(type: $0["type"], id: $0["id"], row: try Row.decode($0["json"]), editSeq: $0["editSeq"])
            }
        }
    }

    func markPushed(_ pushed: [Pending]) throws {
        try db.write { db in
            for p in pushed {
                try db.execute(sql: "UPDATE syncRow SET dirty = 0 WHERE type = ? AND id = ? AND editSeq = ?",
                               arguments: [p.type, p.id, p.editSeq])
            }
        }
    }

    /// Apply server rows (verbatim). Returns how many were applied; dirty local rows are kept.
    @discardableResult
    func applyPulled(_ changes: [String: [Row]]) throws -> Int {
        try db.write { db in
            var applied = 0
            for (type, rows) in changes {
                for row in rows {
                    guard case .string(let rawId) = row["id"] else { continue }
                    let id = rawId.lowercased()
                    let dirty = try Bool.fetchOne(db, sql: "SELECT dirty FROM syncRow WHERE type = ? AND id = ?", arguments: [type, id]) ?? false
                    if dirty { continue }
                    try db.execute(sql: """
                        INSERT INTO syncRow (type, id, json, dirty, editSeq) VALUES (?, ?, ?, 0, 0)
                        ON CONFLICT(type, id) DO UPDATE SET json = excluded.json
                        """, arguments: [type, id, try row.encoded()])
                    applied += 1
                }
            }
            return applied
        }
    }

    func meta(_ key: String) throws -> String? {
        try db.read { try String.fetchOne($0, sql: "SELECT value FROM meta WHERE key = ?", arguments: [key]) }
    }

    func setMeta(_ key: String, _ value: String?) throws {
        try db.write { try $0.execute(sql: "INSERT OR REPLACE INTO meta (key, value) VALUES (?, ?)", arguments: [key, value]) }
    }
}

typealias SQLiteRow = GRDB.Row

/// Decodes a row's `isDeleted` flag and the model from the same JSON in one pass.
private struct Live<T: Decodable>: Decodable {
    let deleted: Bool
    let value: T
    private enum K: String, CodingKey { case isDeleted }
    init(from d: Decoder) throws {
        deleted = (try? d.container(keyedBy: K.self).decode(Bool.self, forKey: .isDeleted)) ?? false
        value = try T(from: d)
    }
}

enum StoreError: LocalizedError {
    case missing(String, String)
    var errorDescription: String? {
        switch self { case .missing(let t, let id): "No \(t) row \(id)." }
    }
}

import Foundation

/// A lossless JSON value. Rows are kept exactly as the server sent them so an edit can change a
/// few fields and push the rest back untouched — the server overwrites every column on push, so
/// a field this app doesn't model must still be sent or it gets nulled. Integers stay integers
/// (a C# int field rejects "2200.0").
enum JSON: Codable, Hashable {
    case null
    case bool(Bool)
    case int(Int64)
    case double(Double)
    case string(String)
    case array([JSON])
    case object([String: JSON])

    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() { self = .null }
        else if let b = try? c.decode(Bool.self) { self = .bool(b) }
        else if let i = try? c.decode(Int64.self) { self = .int(i) }
        else if let d = try? c.decode(Double.self) { self = .double(d) }
        else if let s = try? c.decode(String.self) { self = .string(s) }
        else if let a = try? c.decode([JSON].self) { self = .array(a) }
        else { self = .object(try c.decode([String: JSON].self)) }
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        switch self {
        case .null: try c.encodeNil()
        case .bool(let b): try c.encode(b)
        case .int(let i): try c.encode(i)
        case .double(let d): try c.encode(d)
        case .string(let s): try c.encode(s)
        case .array(let a): try c.encode(a)
        case .object(let o): try c.encode(o)
        }
    }

    var isScalar: Bool {
        switch self { case .array, .object: false; default: true }
    }
}

typealias Row = [String: JSON]

extension Row {
    /// What's safe to push: scalar columns only. Arrays/objects in a pulled row are computed
    /// properties (`items`, `tagList`) or navigation collections (`sets`, `exercises`). Pushing
    /// `items` back would even overwrite an edited `itemsJson` (the C# setter re-serializes it).
    var pushable: Row { filter { $0.value.isScalar } }

    func encoded() throws -> Data { try JSONEncoder().encode(self) }
    static func decode(_ data: Data) throws -> Row { try JSONDecoder().decode(Row.self, from: data) }
}

/// Wire formats .NET reads and writes: local DateTime without a zone ("2026-10-23T12:30:00"),
/// UTC with "Z", and DateOnly ("2026-10-23").
enum Wire {
    static func local(_ d: Date) -> JSON { .string(format(d, "yyyy-MM-dd'T'HH:mm:ss", .current)) }
    static func utc(_ d: Date) -> JSON { .string(format(d, "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", TimeZone(identifier: "UTC")!)) }
    static func day(_ d: Day) -> JSON { .string(String(format: "%04d-%02d-%02d", d.y, d.m, d.d)) }

    private static func format(_ d: Date, _ pattern: String, _ tz: TimeZone) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX"); f.timeZone = tz; f.dateFormat = pattern
        return f.string(from: d)
    }

    /// Parses .NET DateTime: optional 1–7 fractional digits, optional "Z" / offset-less local.
    static func parse(_ s: String) -> Date? {
        var str = s, utc = false
        if str.hasSuffix("Z") { utc = true; str.removeLast() }
        if let dot = str.firstIndex(of: ".") {
            let frac = str[str.index(after: dot)...].prefix(3)
            str = String(str[..<dot]) + "." + frac.padding(toLength: 3, withPad: "0", startingAt: 0)
        } else { str += ".000" }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSS"
        f.timeZone = utc ? TimeZone(identifier: "UTC") : .current
        return f.date(from: str)
    }

    static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .custom { dec in
            let s = try dec.singleValueContainer().decode(String.self)
            guard let date = parse(s) else {
                throw DecodingError.dataCorrupted(.init(codingPath: dec.codingPath, debugDescription: "Bad date \(s)"))
            }
            return date
        }
        return d
    }()
}

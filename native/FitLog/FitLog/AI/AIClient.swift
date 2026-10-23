import Foundation
import UIKit

/// Server AI proxy (/api/v1/ai/generate): the provider keys stay on the server, same as the web.
/// Mistral (Pixtral) first, Gemini fallback — chosen server-side.
@MainActor
final class AIClient {
    private let auth: Auth
    init(auth: Auth) { self.auth = auth }

    struct LabelFacts {
        var description: String?
        var servingSize: String?
        var macros: Macros
        var isEmpty: Bool { macros.calories == nil && macros.proteinG == nil && macros.carbsG == nil }
    }

    /// Read a Nutrition Facts label (one serving, as printed). `prompt` is the user's synced
    /// override for "read_label" when they have one.
    func readLabel(_ image: UIImage, prompt: String?) async throws -> LabelFacts? {
        guard let jpeg = image.downscaled(maxSide: 1600).jpegData(compressionQuality: 0.8) else { return nil }
        let text = try await generate(prompt ?? Self.readLabelPrompt, jpeg: jpeg)
        guard let obj = Self.parseObject(text) else { return nil }
        let facts = LabelFacts(
            description: obj["foodDescription"] as? String, servingSize: obj["servingSize"] as? String,
            macros: Macros(calories: Self.int(obj["calories"]), proteinG: Self.num(obj["proteinG"]), carbsG: Self.num(obj["carbsG"]),
                           sugarG: Self.num(obj["sugarG"]), addedSugarG: Self.num(obj["addedSugarG"]), fatG: Self.num(obj["fatG"]),
                           sodiumMg: Self.int(obj["sodiumMg"]), fiberG: Self.num(obj["fiberG"])))
        return facts.isEmpty ? nil : facts
    }

    private func generate(_ prompt: String, jpeg: Data?) async throws -> String {
        var req = URLRequest(url: URL(string: "api/v1/ai/generate", relativeTo: Config.apiBase)!)
        req.httpMethod = "POST"
        req.timeoutInterval = 90
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue("Bearer \(try await auth.token())", forHTTPHeaderField: "Authorization")
        var body: [String: String] = ["prompt": prompt]
        if let jpeg { body["imageJpegBase64"] = jpeg.base64EncodedString() }
        req.httpBody = try JSONEncoder().encode(body)
        let (data, resp) = try await URLSession.shared.data(for: req)
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        let obj = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
        guard code == 200, let text = obj?["text"] as? String else {
            throw URLError(.badServerResponse, userInfo: [NSLocalizedDescriptionKey:
                (obj?["error"] as? String) ?? "AI request failed (HTTP \(code))"])
        }
        return text
    }

    /// The model's reply as a JSON object, tolerating a ```json fence around it.
    nonisolated static func parseObject(_ text: String) -> [String: Any]? {
        var t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if let a = t.firstIndex(of: "{"), let b = t.lastIndex(of: "}") { t = String(t[a...b]) }
        return (try? JSONSerialization.jsonObject(with: Data(t.utf8))) as? [String: Any]
    }

    // Lenient like the C# IOf/DOf: numbers or numeric strings ("12g" → 12).
    nonisolated static func num(_ v: Any?) -> Double? {
        if let n = v as? NSNumber, !(v is Bool) { return n.doubleValue }
        if let s = v as? String { return Double(s.filter { "0123456789.".contains($0) }) }
        return nil
    }
    nonisolated static func int(_ v: Any?) -> Int? { num(v).map { Int($0.dotnetRounded) } }

    /// Label servings to log for a drink: ounces drunk ÷ label serving (fl oz, oz or mL).
    nonisolated static func servings(drankOunces: Double?, servingSize: String?) -> Double {
        guard let drank = drankOunces, drank > 0, let s = servingSize?.lowercased() else { return 1 }
        func first(_ pattern: String) -> Double? {
            guard let r = s.range(of: pattern, options: .regularExpression) else { return nil }
            return Double(s[r].filter { "0123456789.".contains($0) })
        }
        let serving = first(#"\d+(\.\d+)?\s*fl\s*oz"#) ?? first(#"\d+(\.\d+)?\s*oz"#)
            ?? first(#"\d+(\.\d+)?\s*ml"#).map { ($0 / 29.5735).dotnetRounded(1) }
        guard let serving, serving > 0 else { return 1 }
        return (drank / serving).dotnetRounded(2)
    }

    /// Default "read_label" prompt — copied verbatim from C# PromptDefaults.ReadLabel.
    nonisolated static let readLabelPrompt = """
Read a packaged-food Nutrition Facts label from this photo AND tag/rate the food. Extract the values for ONE serving as printed. Respond with ONLY this JSON object (null for anything not legible):
{ "foodDescription": "<short product name, e.g. 'Greek yogurt' or 'whey protein'>", "servingSize": "<as printed>", "calories": <int>, "proteinG": <num>, "carbsG": <num>, "sugarG": <num>, "addedSugarG": <num>, "fatG": <num>, "sodiumMg": <int>, "fiberG": <num>, "tags": ["<existing tags that apply>"], "newTag": "<one new tag or null>", "stars": <1-5>, "starReason": "<=8 words, encouraging>" }
Use 'Total Sugars' for sugarG, 'Includes Xg Added Sugars' for addedSugarG (null if not listed), and 'Total Fat' for fatG. Numbers only — strip units. If the image is not a nutrition label, return all nulls. Base tags and the star rating on the ACTUAL macros you read (per serving), not guesses.
"""
}

extension UIImage {
    func downscaled(maxSide: CGFloat) -> UIImage {
        let scale = min(1, maxSide / max(size.width, size.height))
        guard scale < 1 else { return self }
        let target = CGSize(width: size.width * scale, height: size.height * scale)
        let fmt = UIGraphicsImageRendererFormat(); fmt.scale = 1
        return UIGraphicsImageRenderer(size: target, format: fmt).image { _ in draw(in: CGRect(origin: .zero, size: target)) }
    }
}

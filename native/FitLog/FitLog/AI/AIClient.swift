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

    struct PlateEstimate {
        var description: String?
        var items: [MealItem]
        var totals: Macros
        var stars: Int?
        var tags: [String]
        var adviceNeeded = false
        var adviceMessage: String?
        var adviceSuggestion: String?
        var adjusted: Macros?
    }

    /// Estimate a plate from a photo — the C# EstimateMealFromPhotoAsync prompt: optional user
    /// correction first, then the estimate template, the tagging rules, macro targets and today's
    /// context (so portion advice can compare against today's totals).
    func estimatePlate(_ image: UIImage, hint: String?, template: String?, rules: String?, vocabulary: [String],
                       context: String, mealType: String?) async throws -> PlateEstimate? {
        guard let jpeg = image.downscaled(maxSide: 1600).jpegData(compressionQuality: 0.8) else { return nil }
        var p = ""
        if let h = hint?.trimmingCharacters(in: .whitespacesAndNewlines), !h.isEmpty {
            p += "IMPORTANT USER CORRECTION — treat as authoritative and re-estimate accordingly: \"\(h)\"\n"
        }
        p += (template ?? Self.estimatePhotoPrompt) + "\n"
        p += (rules ?? Self.mealTaggingRules).replacingOccurrences(of: "{vocabulary}", with: vocabulary.joined(separator: " | ")) + "\n"
        p += context
        p += "MEAL TYPE: \(mealType ?? "(infer from the time in NOW)")\n"
        let text = try await generate(p, jpeg: jpeg)
        guard let o = Self.parseObject(text) else { return nil }
        func macros(_ d: [String: Any]?) -> Macros {
            Macros(calories: Self.int(d?["calories"]), proteinG: Self.num(d?["proteinG"]), carbsG: Self.num(d?["carbsG"]),
                   sugarG: Self.num(d?["sugarG"]), addedSugarG: Self.num(d?["addedSugarG"]), fatG: Self.num(d?["fatG"]),
                   sodiumMg: Self.int(d?["sodiumMg"]), fiberG: Self.num(d?["fiberG"]))
        }
        let items = ((o["items"] as? [[String: Any]]) ?? []).compactMap { it -> MealItem? in
            guard let n = (it["name"] as? String)?.trimmingCharacters(in: .whitespaces), !n.isEmpty else { return nil }
            return macros(it).item(named: n)
        }
        let totals = macros(o)
        guard !items.isEmpty || !totals.isEmpty else { return nil }
        var tags = (o["tags"] as? [String]) ?? []
        if let nt = (o["newTag"] as? String)?.trimmingCharacters(in: .whitespaces), !nt.isEmpty { tags.append(nt) }
        let adv = o["portionAdvice"] as? [String: Any]
        return PlateEstimate(description: o["foodDescription"] as? String, items: items, totals: totals,
                             stars: Self.int(o["stars"]).map { min(5, max(1, $0)) }, tags: tags,
                             adviceNeeded: (adv?["needed"] as? Bool) ?? false,
                             adviceMessage: adv?["message"] as? String, adviceSuggestion: adv?["suggestion"] as? String,
                             adjusted: (adv?["adjusted"] as? [String: Any]).map(macros))
    }

    /// The coaching review (prompt built by Coach.prompt).
    func coach(_ prompt: String) async throws -> Coach.Report? {
        Self.parseObject(try await generate(prompt, jpeg: nil)).flatMap { Coach.Report.parse($0, at: Date()) }
    }

    func dailyCheck(_ prompt: String) async throws -> Coach.DailyCheck? {
        Self.parseObject(try await generate(prompt, jpeg: nil)).flatMap { Coach.DailyCheck.parse($0, at: Date()) }
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

    /// Default "estimate_photo" and "meal_tagging_rules" prompts — verbatim from C# PromptDefaults.
    nonisolated static let estimatePhotoPrompt = """
Estimate the nutrition of the meal in this photo AND tag/rate it. Identify each food, estimate its portion, give EACH item its own macros in "items", then TOTAL them for the whole plate/bowl. Treat a composite dish as ONE item — a sandwich, burger, casserole, burrito, salad, bowl, or stir-fry is a single item, NOT its ingredients; only split clearly separate foods on the plate (e.g. meatloaf + mashed potatoes + corn = three items). Respond with ONLY this JSON object (null for anything you truly can't estimate):
{ "foodDescription": "<short, e.g. 'meatloaf, mashed potatoes, green beans'>", "servingSize": "whole plate (estimate)", "items": [ { "name": "<one food>", "calories": <int>, "proteinG": <num>, "carbsG": <num>, "sugarG": <num>, "addedSugarG": <num>, "fatG": <num>, "sodiumMg": <int>, "fiberG": <num> } ], "calories": <int>, "proteinG": <num>, "carbsG": <num>, "sugarG": <num>, "addedSugarG": <num>, "fatG": <num>, "sodiumMg": <int>, "fiberG": <num>, "tags": ["<existing tags that apply>"], "newTag": "<one new tag or null>", "stars": <1-5>, "starReason": "<=8 words, encouraging>", "portionAdvice": { "needed": true or false, "message": "<which macro today is high and which plate item(s) drive it, else empty>", "suggestion": "<one concrete trim: skip or reduce a NAMED item from items by a rough %, else empty>", "adjusted": { "calories": <int>, "proteinG": <num>, "carbsG": <num>, "sugarG": <num>, "addedSugarG": <num>, "fatG": <num>, "sodiumMg": <int>, "fiberG": <num> } } }
The top-level totals MUST equal the sum of "items". Numbers are units stripped; the totals are the whole plate. If it isn't a photo of food, return all nulls.
CALIBRATION — photo estimates tend to run HIGH; do NOT overestimate. Assume STANDARD home portions unless the plate is clearly oversized, and do NOT add calories for oil, butter, or sauce you cannot actually see. Use these per-serving anchors and pick the MIDDLE of the range, never the top:
  - cooked meat/protein, palm-sized 3-5 oz: 150-300 kcal
  - mashed potatoes ~1 cup: 200-250 kcal; rice/pasta ~1 cup: ~200 kcal
  - non-starchy veg (green beans, broccoli, salad) ~1 cup: 40-70 kcal
  - a slice of bread ~80 kcal; a pat of butter ~35 kcal
A normal meat-and-two-sides dinner plate totals roughly 500-750 kcal. Only exceed ~900 if the plate is clearly large or obviously fried, breaded, heavily sauced, or cheesy. Base tags and the star rating on the estimated macros.
PER-MEAL BALANCE CHECK — the DAILY MACRO/HYDRATION TARGETS and TODAY'S TOTALS SO FAR are below, along with the MEAL TYPE and the current time in NOW. Derive a rough BASELINE for this plate by splitting the daily calorie, carb, and sugar targets across a normal day's meals, weighted by meal type: breakfast ~25%, lunch ~35%, dinner ~35%, snack ~10%. Protein is a DAILY total the user wants to REACH — do NOT cap or penalize a protein-heavy plate. Set portionAdvice.needed=true and suggest a trim when EITHER (a) this plate clearly EXCEEDS its per-meal baseline for calories, carbs, or sugar, OR (b) adding it to today's totals would push a macro OVER its daily range. When flagging: name the plate item(s) driving the overage in "suggestion" (drop, or reduce by a rough %), say briefly why in "message" (e.g. "Well over a dinner-sized carb share"), and put the trimmed macros — brought within the baseline — in "adjusted". A plate at or under its baseline needs NO advice; do not nag. If NO targets are provided below, set needed=false, leave message/suggestion empty, and set "adjusted" equal to the full estimate.
"""

    nonisolated static let mealTaggingRules = """
Existing tags (use these exact strings, strongly prefer them): {vocabulary}
Only include tags well supported by the meal; when unsure, leave a tag out. Most meals need just 0-2 tags. Do NOT apply a tag by default — each must clearly fit.
'High sodium' specifically: reserve it for foods genuinely high in salt — cured/processed meats (bacon, deli, sausage), canned/instant foods, pizza, chips, or fast-food/restaurant items known to be salt-heavy. A home-cooked rotisserie-chicken/rice/bean bowl or a plain hamburger is NOT automatically high sodium. When unsure, leave it off.
Judge the meal AS A WHOLE for sodium — a lean/grilled main (e.g. grilled chicken sandwich) is NOT 'high sodium' just because a side like fries is salty. Only tag High sodium when the meal is PREDOMINANTLY salt-heavy, not when one minor side is.
'High sugar' specifically: keep sugar in PROPORTION. A snack or treat with a modest amount (roughly <=15g sugar — a banana is ~14g, a few graham crackers/cookies are single-digit grams) is NORMAL — do NOT tag 'High sugar' and do NOT lower stars for it. Reserve 'High sugar' for items genuinely loaded with sugar (candy, soda, dessert-sized sweets, ~25g+). When unsure, leave it off.
A newTag must be short (1-3 words, e.g. 'High sugar'), broadly reusable, and not a synonym of an existing tag. Tags describe nutritional quality or food source. The entry already records its type (breakfast/lunch/dinner/snack/drink), time, and portion — NEVER suggest those as tags.
STARS (1-5) = how well this meal fits the user's goals below, encouraging and moderation-minded: a sensible everyday meal is 3-4; a great goal-aligned choice is 5; a clear off-plan splurge is 1-2. A reasonable treat or modest snack is NOT a failure — a graham-cracker-sized snack with single-digit sugar is a normal 3-4, not a 1-2. Do not dock stars or write a cautionary reason for modest sugar/sodium; reserve low scores for genuinely large portions or truly indulgent items. If no goals are set, rate general balance/protein.
"""

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

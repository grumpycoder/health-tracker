namespace FitRecoveryLog.Services;

/// <summary>
/// Offline keyword-match tag suggestions — used ONLY when the Gemini call can't
/// happen (no key, offline, API error). Deliberately small and conservative;
/// it fills what it can and stays quiet otherwise.
/// </summary>
public static class MealTagFallback
{
    private static readonly (string Tag, string[] Keywords)[] Rules =
    {
        ("High protein", new[] { "chicken", "turkey", "egg", "steak", "beef", "fish", "salmon", "tuna", "pork", "shrimp", "protein", "jerky" }),
        ("High carb", new[] { "rice", "pasta", "bread", "potato", "fries", "cereal", "mini-wheats", "oatmeal", "pancake", "bagel", "tortilla", "noodle" }),
        ("High sodium", new[] { "mcdonald", "wendy", "burger king", "taco bell", "pizza", "ramen", "chips", "bacon", "deli", "fries", "hot dog", "sausage" }),
        ("Sweet drink", new[] { "sweet tea", "soda", "coke", "dr pepper", "sprite", "lemonade", "juice", "frappuccino", "milkshake" }),
        ("Restaurant meal", new[] { "mcdonald", "wendy", "burger king", "taco bell", "chipotle", "subway", "restaurant", "takeout", "take-out", "drive-thru", "drive thru" }),
        ("Home-cooked", new[] { "homemade", "home-cooked", "home cooked" }),
    };

    /// <summary>Tags whose keywords appear in the meal text (case-insensitive).</summary>
    public static List<string> Suggest(string text) =>
        Rules.Where(r => r.Keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
             .Select(r => r.Tag).ToList();
}

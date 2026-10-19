using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Domain.Nutrition;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Everything the web knows about foods, built the same way as the phone's typeahead:
/// curated saved foods first, then the newest logged item breakdown, then whole meals by name.
/// Macros are per serving.</summary>
public sealed class FoodLibrary
{
    private readonly Dictionary<string, Persistence.MealItemPortion> _defaults;

    public IReadOnlyList<string> Names { get; }
    public IReadOnlyDictionary<string, SavedFood> Saved { get; }

    private FoodLibrary(Dictionary<string, Persistence.MealItemPortion> defaults, Dictionary<string, SavedFood> saved,
        IReadOnlySet<string>? hidden = null)
    {
        _defaults = defaults;
        Saved = saved;
        Names = defaults.Keys.Where(n => hidden?.Contains(n) != true)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static FoodLibrary Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));

    public static async Task<FoodLibrary> LoadAsync(AppState state, SavedFoodService savedFoods, IReadOnlySet<string>? hidden = null)
    {
        var pull = await state.DataAsync();
        var meals = WebSyncClient.Rows<Persistence.MealEntry>(pull).OrderByDescending(m => m.Time).ToList();
        var drinks = WebSyncClient.Rows<Persistence.DrinkEntry>(pull).OrderByDescending(d => d.Time).ToList();
        var defaults = new Dictionary<string, Persistence.MealItemPortion>(StringComparer.OrdinalIgnoreCase);

        // Lowest priority first; later sources overwrite. Within a source rows are newest-first,
        // so only the first (newest) use of a name is kept.
        void FromSource(IEnumerable<(string Name, Persistence.MealItemPortion Macros)> uses)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, m) in uses)
            {
                var n = name.Trim();
                if (n.Length == 0 || !seen.Add(n)) continue;
                defaults[n] = Copy(m, n);
            }
        }

        FromSource(meals.Select(m => (m.Description ?? "", new Persistence.MealItemPortion
        {
            Calories = m.Calories, ProteinG = m.ProteinG, CarbsG = m.CarbsG, SugarG = m.SugarG,
            AddedSugarG = m.AddedSugarG, FatG = m.FatG, SodiumMg = m.SodiumMg, FiberG = m.FiberG,
        })));
        FromSource(meals.Select(m => (m.Time, m.Items)).Concat(drinks.Select(d => (d.Time, d.Items)))
            .OrderByDescending(x => x.Time)
            .SelectMany(x => x.Items.Select(it => (it.Name ?? "", it))));

        var saved = (await savedFoods.ListAsync()).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var sf in saved.Values)
        {
            var m = sf.Macros;
            defaults[sf.Name] = new Persistence.MealItemPortion
            {
                Name = sf.Name, Calories = m.Calories, ProteinG = m.ProteinG, CarbsG = m.CarbsG, SugarG = m.SugarG,
                AddedSugarG = m.AddedSugarG, FatG = m.FatG, SodiumMg = m.SodiumMg, FiberG = m.FiberG, Portion = 1,
            };
        }
        return new FoodLibrary(defaults, saved, hidden);
    }

    /// <summary>A fresh one-serving item for <paramref name="name"/> (no macros if the name is new).</summary>
    public Persistence.MealItemPortion ItemFor(string name)
    {
        var n = name.Trim();
        return _defaults.TryGetValue(n, out var d) ? Copy(d, d.Name) : new Persistence.MealItemPortion { Name = n, Portion = 1 };
    }

    /// <summary>Reuse a stored breakdown at one serving each; curated saved-food macros win.</summary>
    public List<Persistence.MealItemPortion> Restore(IEnumerable<Persistence.MealItemPortion> stored) =>
        stored.Select(x => Saved.ContainsKey(x.Name.Trim()) ? ItemFor(x.Name) : Copy(x, x.Name)).ToList();

    public static Persistence.MealItemPortion Copy(Persistence.MealItemPortion x, string name, double portion = 1) => new()
    {
        Name = name, Calories = x.Calories, ProteinG = x.ProteinG, CarbsG = x.CarbsG, SugarG = x.SugarG,
        AddedSugarG = x.AddedSugarG, FatG = x.FatG, SodiumMg = x.SodiumMg, FiberG = x.FiberG, Portion = portion,
    };

    public static List<string> Suggest(IEnumerable<string> names, string? query, int max = 8)
    {
        var q = query?.Trim();
        if (string.IsNullOrEmpty(q)) return new();
        return names.Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n.Length).Take(max).ToList();
    }
}

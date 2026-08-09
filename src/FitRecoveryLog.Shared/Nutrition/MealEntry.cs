using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace FitRecoveryLog.Data;

public class MealEntry : EntityBase
{
    public DateTime Time { get; set; } = DateTime.Now;
    public MealType MealType { get; set; } = MealType.Snack;
    public string Description { get; set; } = "";
    public string? PortionNote { get; set; }

    /// <summary>CSV of tags (High protein, Restaurant meal, etc.). Use <see cref="TagList"/>.</summary>
    public string? Tags { get; set; }
    public Satiety Satiety { get; set; } = Satiety.Unset;
    /// <summary>Optional 1-5 "fit with your goals" score from the ✨ tag suggester.</summary>
    public int? QualityStars { get; set; }

    // Macros for the whole meal as eaten (label per-serving × servings eaten).
    // Populated by the nutrition-label scan or entered by hand; all optional.
    public int? Calories { get; set; }
    public double? ProteinG { get; set; }
    public double? CarbsG { get; set; }
    public double? SugarG { get; set; }
    public double? FatG { get; set; }
    public int? SodiumMg { get; set; }
    public double? FiberG { get; set; }
    /// <summary>Added sugars (label line), distinct from total SugarG.</summary>
    public double? AddedSugarG { get; set; }

    /// <summary>JSON of the plate's per-item breakdown (each item's 100%-contribution macros + the
    /// portion eaten), so the item sliders can be re-adjusted after the meal is saved. The meal's
    /// macro totals above stay the source of truth for tracking; this is just the editable breakdown.</summary>
    public string? ItemsJson { get; set; }

    [NotMapped]
    public IReadOnlyList<string> TagList => CsvField.Split(Tags);

    [NotMapped]
    public List<MealItemPortion> Items
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ItemsJson)) return new();
            try { return JsonSerializer.Deserialize<List<MealItemPortion>>(ItemsJson) ?? new(); }
            catch (JsonException) { return new(); }
        }
        set => ItemsJson = value is { Count: > 0 } ? JsonSerializer.Serialize(value) : null;
    }

    /// <summary>Compact per-item macro breakdown for AI prompts (each item's macros × portion eaten),
    /// so the analysis can attribute macro overages to specific plate items. Empty when no breakdown.</summary>
    public string ItemsSummary()
    {
        var items = Items;
        if (items.Count == 0) return "";
        return string.Join(", ", items.Select(One));

        static string One(MealItemPortion it)
        {
            var p = it.Portion;
            var parts = new List<string>();
            if (it.Calories is int c) parts.Add($"{Math.Round(c * p)}cal");
            if (it.CarbsG is double cb) parts.Add($"{Math.Round(cb * p, 1)}C");
            if (it.SugarG is double su) parts.Add($"{Math.Round(su * p, 1)}sugar");
            if (it.FatG is double f) parts.Add($"{Math.Round(f * p, 1)}F");
            if (it.ProteinG is double pr) parts.Add($"{Math.Round(pr * p, 1)}P");
            var pct = p < 0.999 ? $"@{(int)Math.Round(p * 100)}%" : "";
            return $"{it.Name}{pct}({string.Join("/", parts)})";
        }
    }
}

/// <summary>One plate item: its macros at 100% (the full contribution) plus the fraction eaten.
/// The meal total is the sum of each item's macros × <see cref="Portion"/>.</summary>
public sealed class MealItemPortion
{
    public string Name { get; set; } = "";
    public int? Calories { get; set; }
    public double? ProteinG { get; set; }
    public double? CarbsG { get; set; }
    public double? SugarG { get; set; }
    public double? AddedSugarG { get; set; }
    public double? FatG { get; set; }
    public int? SodiumMg { get; set; }
    public double? FiberG { get; set; }
    public double Portion { get; set; } = 1;
}

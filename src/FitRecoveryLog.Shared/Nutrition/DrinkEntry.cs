using System.ComponentModel.DataAnnotations.Schema;

namespace FitRecoveryLog.Data;

/// <summary>Drinks tracked separately so we can total tea/coffee/soda intake easily.</summary>
public class DrinkEntry : EntityBase
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
    public double? Ounces { get; set; }
    /// <summary>For coffee: number of sugar cubes/teaspoons added.</summary>
    public int? SugarCount { get; set; }
    public string? Tags { get; set; }

    // Macros as consumed (from a scanned drink label, × servings); all optional.
    // Packaged drinks (protein shakes, juice, soda) carry real macros; coffee with
    // added sugar keeps using SugarCount instead.
    public int? Calories { get; set; }
    public double? ProteinG { get; set; }
    public double? CarbsG { get; set; }
    public double? SugarG { get; set; }
    public double? FatG { get; set; }
    public int? SodiumMg { get; set; }
    public double? FiberG { get; set; }
    /// <summary>Added sugars (label line); coffee's SugarCount also counts as added sugar.</summary>
    public double? AddedSugarG { get; set; }

    [NotMapped]
    public IReadOnlyList<string> TagList => CsvField.Split(Tags);
}

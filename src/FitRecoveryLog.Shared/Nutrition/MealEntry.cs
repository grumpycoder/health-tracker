using System.ComponentModel.DataAnnotations.Schema;

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

    [NotMapped]
    public IReadOnlyList<string> TagList => CsvField.Split(Tags);
}

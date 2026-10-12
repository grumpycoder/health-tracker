namespace FitRecoveryLog.Data;

/// <summary>A user-curated food in the saved-foods library: the per-serving macros to use
/// whenever the food is logged or added to a meal as an item. Only foods whose macros the
/// user has edited get a row — the rest fall back to their most recent log. Matched by
/// <see cref="Name"/> case-insensitively; no unique index, so two devices creating the
/// same name offline can't collide on sync (the newest row wins on read).</summary>
public class SavedFoodEntry : EntityBase
{
    public string Name { get; set; } = "";
    public string? ServingNote { get; set; }

    public int? Calories { get; set; }
    public double? ProteinG { get; set; }
    public double? CarbsG { get; set; }
    public double? SugarG { get; set; }
    public double? AddedSugarG { get; set; }
    public double? FatG { get; set; }
    public int? SodiumMg { get; set; }
    public double? FiberG { get; set; }
}

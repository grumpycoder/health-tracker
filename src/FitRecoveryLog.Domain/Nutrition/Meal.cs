using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Nutrition;

/// <summary>
/// A logged meal. A single-entity aggregate whose richness lives in its value objects
/// (<see cref="Nutrition.Macros"/>, <see cref="Common.Tags"/>) and guarded fields.
/// </summary>
public sealed class Meal
{
    public Guid Id { get; }
    public DateTime Time { get; private set; }
    public MealType MealType { get; private set; }
    public string Description { get; private set; }
    public string? PortionNote { get; private set; }
    public Satiety Satiety { get; private set; }
    public int? QualityStars { get; private set; }
    public Macros Macros { get; private set; }
    public Tags Tags { get; private set; }

    private Meal(Guid id, DateTime time, MealType mealType, string description, string? portionNote,
        Satiety satiety, int? qualityStars, Macros macros, Tags tags)
    {
        Id = id; Time = time; MealType = mealType; Description = description; PortionNote = portionNote;
        Satiety = satiety; Macros = macros; Tags = tags;
        SetQualityStars(qualityStars);
    }

    public static Meal Create(DateTime time, MealType mealType, string? description = null) =>
        new(Guid.NewGuid(), time, mealType, (description ?? "").Trim(), null, Satiety.Unset, null, Macros.None, Tags.Empty);

    public static Meal Rehydrate(Guid id, DateTime time, MealType mealType, string description, string? portionNote,
        Satiety satiety, int? qualityStars, Macros macros, Tags tags) =>
        new(id, time, mealType, description ?? "", portionNote, satiety,
            qualityStars is >= 1 and <= 5 ? qualityStars : null, macros, tags);

    public void SetTime(DateTime time) => Time = time;
    public void SetMealType(MealType type) => MealType = type;
    public void SetDescription(string? description) => Description = (description ?? "").Trim();
    public void SetPortionNote(string? note) => PortionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    public void SetSatiety(Satiety satiety) => Satiety = satiety;
    public void SetMacros(Macros macros) => Macros = macros;
    public void SetTags(Tags tags) => Tags = tags;

    /// <summary>Optional 1-5 "fit with goals" rating; anything outside that clears it.</summary>
    public void SetQualityStars(int? stars) =>
        QualityStars = stars is >= 1 and <= 5 ? stars : null;
}

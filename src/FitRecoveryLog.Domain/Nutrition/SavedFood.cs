namespace FitRecoveryLog.Domain.Nutrition;

/// <summary>A food in the saved-foods library with its per-serving <see cref="Nutrition.Macros"/>.
/// The source of truth for a food's macros when it's logged again or added to a meal as an
/// item. Name is required and trimmed.</summary>
public sealed class SavedFood
{
    public Guid Id { get; }
    public string Name { get; private set; }
    public string? ServingNote { get; private set; }
    public Macros Macros { get; private set; }

    private SavedFood(Guid id, string name, string? servingNote, Macros macros)
    {
        Id = id; Name = name; ServingNote = servingNote; Macros = macros;
    }

    public static SavedFood Create(string name, Macros macros, string? servingNote = null)
    {
        var food = new SavedFood(Guid.NewGuid(), "", null, macros);
        food.Rename(name);
        food.SetServingNote(servingNote);
        return food;
    }

    public static SavedFood Rehydrate(Guid id, string name, string? servingNote, Macros macros) =>
        new(id, name ?? "", servingNote, macros);

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Food name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetServingNote(string? note) =>
        ServingNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public void SetMacros(Macros macros) => Macros = macros;
}

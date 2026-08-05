using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Nutrition;

/// <summary>A logged drink. Single-entity aggregate; richness in its <see cref="Nutrition.Macros"/>
/// and <see cref="Common.Tags"/> value objects, with non-negative amount/sugar guards.</summary>
public sealed class Drink
{
    public Guid Id { get; }
    public DateTime Time { get; private set; }
    public string Description { get; private set; }
    public double? Ounces { get; private set; }
    public int? SugarCount { get; private set; }
    public Macros Macros { get; private set; }
    public Tags Tags { get; private set; }

    private Drink(Guid id, DateTime time, string description, double? ounces, int? sugarCount, Macros macros, Tags tags)
    {
        Id = id; Time = time; Description = description; Macros = macros; Tags = tags;
        SetOunces(ounces);
        SetSugarCount(sugarCount);
    }

    public static Drink Create(DateTime time, string? description = null) =>
        new(Guid.NewGuid(), time, (description ?? "").Trim(), null, null, Macros.None, Tags.Empty);

    public static Drink Rehydrate(Guid id, DateTime time, string description, double? ounces, int? sugarCount, Macros macros, Tags tags) =>
        new(id, time, description ?? "", ounces, sugarCount, macros, tags);

    public void SetTime(DateTime time) => Time = time;
    public void SetDescription(string? description) => Description = (description ?? "").Trim();
    public void SetMacros(Macros macros) => Macros = macros;
    public void SetTags(Tags tags) => Tags = tags;

    public void SetOunces(double? ounces)
    {
        if (ounces < 0) throw new ArgumentOutOfRangeException(nameof(ounces), "Ounces cannot be negative.");
        Ounces = ounces;
    }

    public void SetSugarCount(int? sugarCount)
    {
        if (sugarCount < 0) throw new ArgumentOutOfRangeException(nameof(sugarCount), "Sugar count cannot be negative.");
        SugarCount = sugarCount;
    }
}

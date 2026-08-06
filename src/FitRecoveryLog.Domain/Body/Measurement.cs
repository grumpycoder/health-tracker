using FitRecoveryLog.Domain.Body.Events;
using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Domain.Body;

/// <summary>
/// A day's body measurements (weight, tape measures, smart-scale body composition). A flat
/// single-entity aggregate saved as a whole; all values optional but non-negative. Recording a
/// weight or waist raises <see cref="MeasurementRecorded"/> so it can be mirrored to Apple Health.
/// </summary>
public sealed class Measurement : AggregateRoot
{
    public Guid Id { get; }
    public DateOnly Date { get; private set; }
    public double? WeightLbs { get; private set; }
    public double? WaistInches { get; private set; }
    public double? ChestInches { get; private set; }
    public double? HipsInches { get; private set; }
    public double? ArmsInches { get; private set; }
    public double? ThighsInches { get; private set; }
    public double? BodyFatPercent { get; private set; }
    public double? MuscleMassLbs { get; private set; }
    public double? VisceralFat { get; private set; }
    public double? BodyWaterPercent { get; private set; }
    public int? BasalMetabolicRate { get; private set; }
    public int? MetabolicAge { get; private set; }
    public string? ClothingFitNotes { get; private set; }

    private Measurement(Guid id, DateOnly date) { Id = id; Date = date; }

    public static Measurement Create(DateOnly date) => new(Guid.NewGuid(), date);

    public static Measurement Rehydrate(Guid id, DateOnly date, double? weight, double? waist, double? chest,
        double? hips, double? arms, double? thighs, double? bodyFat, double? muscle, double? visceral,
        double? water, int? bmr, int? metabolicAge, string? notes)
    {
        var m = new Measurement(id, date);
        // Assign directly — reconstructing persisted state must not raise a domain event.
        m.Assign(weight, waist, chest, hips, arms, thighs, bodyFat, muscle, visceral, water, bmr, metabolicAge, notes);
        return m;
    }

    public void SetDate(DateOnly date) => Date = date;

    /// <summary>Set all measurement values at once (the entry is saved as a whole). Raises
    /// <see cref="MeasurementRecorded"/> when a weight or waist is present.</summary>
    public void Update(double? weight, double? waist, double? chest, double? hips, double? arms, double? thighs,
        double? bodyFat, double? muscle, double? visceral, double? water, int? bmr, int? metabolicAge, string? notes)
    {
        if (new double?[] { weight, waist, chest, hips, arms, thighs, bodyFat, muscle, visceral, water }.Any(v => v < 0)
            || bmr < 0 || metabolicAge < 0)
            throw new ArgumentOutOfRangeException(nameof(weight), "Measurement values cannot be negative.");

        Assign(weight, waist, chest, hips, arms, thighs, bodyFat, muscle, visceral, water, bmr, metabolicAge, notes);

        if (WeightLbs is not null || WaistInches is not null)
            Raise(new MeasurementRecorded(Id, Date, WeightLbs, WaistInches));
    }

    private void Assign(double? weight, double? waist, double? chest, double? hips, double? arms, double? thighs,
        double? bodyFat, double? muscle, double? visceral, double? water, int? bmr, int? metabolicAge, string? notes)
    {
        WeightLbs = weight; WaistInches = waist; ChestInches = chest; HipsInches = hips;
        ArmsInches = arms; ThighsInches = thighs; BodyFatPercent = bodyFat; MuscleMassLbs = muscle;
        VisceralFat = visceral; BodyWaterPercent = water; BasalMetabolicRate = bmr; MetabolicAge = metabolicAge;
        ClothingFitNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}

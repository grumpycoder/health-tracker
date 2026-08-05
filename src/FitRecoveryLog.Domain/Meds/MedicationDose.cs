namespace FitRecoveryLog.Domain.Meds;

/// <summary>A logged medication dose. Name is required; everything else is optional detail.</summary>
public sealed class MedicationDose
{
    public Guid Id { get; }
    public string Name { get; private set; }
    public string? Dose { get; private set; }
    public string? Frequency { get; private set; }
    public DateTime TakenAt { get; private set; }
    public string? InjectionSite { get; private set; }
    public string? ReactionNotes { get; private set; }

    private MedicationDose(Guid id, DateTime takenAt, string name) { Id = id; TakenAt = takenAt; Name = name; }

    public static MedicationDose Create(DateTime takenAt, string name)
    {
        var dose = new MedicationDose(Guid.NewGuid(), takenAt, "");
        dose.SetName(name);
        return dose;
    }

    public static MedicationDose Rehydrate(Guid id, string name, string? dose, string? frequency,
        DateTime takenAt, string? injectionSite, string? reactionNotes) =>
        new(id, takenAt, name ?? "")
        {
            Dose = dose, Frequency = frequency, InjectionSite = injectionSite, ReactionNotes = reactionNotes,
        };

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A medication name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetTakenAt(DateTime takenAt) => TakenAt = takenAt;
    public void SetDose(string? dose) => Dose = Trim(dose);
    public void SetFrequency(string? frequency) => Frequency = Trim(frequency);
    public void SetInjectionSite(string? site) => InjectionSite = Trim(site);
    public void SetReactionNotes(string? notes) => ReactionNotes = Trim(notes);

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

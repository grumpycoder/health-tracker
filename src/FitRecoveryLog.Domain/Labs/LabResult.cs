namespace FitRecoveryLog.Domain.Labs;

/// <summary>A lab result (Testosterone, A1C, PSA, …). Lab name is required.</summary>
public sealed class LabResult
{
    public Guid Id { get; }
    public DateOnly Date { get; private set; }
    public string LabName { get; private set; }
    public double? Value { get; private set; }
    public string? Unit { get; private set; }
    public string? Notes { get; private set; }

    private LabResult(Guid id, DateOnly date, string labName) { Id = id; Date = date; LabName = labName; }

    public static LabResult Create(DateOnly date, string labName)
    {
        var lab = new LabResult(Guid.NewGuid(), date, "");
        lab.SetLabName(labName);
        return lab;
    }

    public static LabResult Rehydrate(Guid id, DateOnly date, string labName, double? value, string? unit, string? notes) =>
        new(id, date, labName ?? "") { Value = value, Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim() };

    public void SetDate(DateOnly date) => Date = date;
    public void SetValue(double? value) => Value = value;
    public void SetUnit(string? unit) => Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public void SetLabName(string labName)
    {
        if (string.IsNullOrWhiteSpace(labName)) throw new ArgumentException("A lab name is required.", nameof(labName));
        LabName = labName.Trim();
    }
}

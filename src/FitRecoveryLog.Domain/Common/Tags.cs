namespace FitRecoveryLog.Domain.Common;

/// <summary>An ordered, de-duplicated set of freeform tags. Value object; round-trips to the
/// CSV form the entries are stored as.</summary>
public sealed class Tags : ValueObject
{
    private readonly List<string> _values;
    public IReadOnlyList<string> Values => _values;

    public static readonly Tags Empty = new(new List<string>());

    private Tags(List<string> values) => _values = values;

    public static Tags FromCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Empty;
        var values = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new Tags(values);
    }

    public string? ToCsv() => _values.Count == 0 ? null : string.Join(",", _values);

    protected override IEnumerable<object?> GetEqualityComponents() => _values.Select(v => v.ToLowerInvariant());
}

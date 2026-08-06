namespace FitRecoveryLog.Data;

/// <summary>Helpers for CSV-backed multi-value string columns.</summary>
public static class CsvField
{
    public static IReadOnlyList<string> Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Join(IEnumerable<string> values) => string.Join(",", values);
}

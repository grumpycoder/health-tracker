using System.Text.Json;
using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

/// <summary>Phone-local set of food names hidden from the meal autocomplete. Persisted in
/// Preferences (not synced) — it only changes which suggestions show on this device; the
/// logged meals themselves are untouched.</summary>
public static class HiddenFoods
{
    private const string Key = "hidden_foods";

    public static HashSet<string> Load()
    {
        var json = Preferences.Default.Get<string?>(Key, null);
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        try { return new(JsonSerializer.Deserialize<List<string>>(json) ?? new(), StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void Save(IEnumerable<string> names) =>
        Preferences.Default.Set(Key,
            JsonSerializer.Serialize(names.Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
}

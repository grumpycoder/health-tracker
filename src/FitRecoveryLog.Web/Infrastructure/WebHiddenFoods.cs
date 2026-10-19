using System.Text.Json;
using Microsoft.JSInterop;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>Foods hidden from suggestions — kept per browser (localStorage), like the phone keeps
/// them per device. Hidden foods stay in history and the Saved Foods page.</summary>
public static class WebHiddenFoods
{
    private const string Key = "fitlog.hiddenFoods";

    public static async Task<HashSet<string>> LoadAsync(IJSRuntime js)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            var names = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<List<string>>(json);
            return new HashSet<string>(names ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception) { return new(StringComparer.OrdinalIgnoreCase); }   // storage blocked → nothing hidden
    }

    public static async Task SaveAsync(IJSRuntime js, IEnumerable<string> names)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(names.ToList())); }
        catch (Exception) { }
    }
}

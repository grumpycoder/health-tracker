using FitRecoveryLog.Application.Ai;
using Microsoft.Maui.Storage;

namespace FitRecoveryLog.Services;

/// <summary>
/// iOS-Keychain-backed <see cref="ILlmKeyStore"/> (falls back to Preferences when SecureStorage
/// is unavailable, e.g. an unsigned Catalyst dev build). Uses the same storage key as before so
/// an already-saved key is found after the refactor.
/// </summary>
public sealed class SecureLlmKeyStore : ILlmKeyStore
{
    // "gemini" → "gemini_api_key" keeps the original storage key (already-saved keys still found);
    // "groq" → "groq_api_key" for the fallback provider.
    private static string KeyName(string provider) => $"{provider}_api_key";

    public async Task<string?> GetAsync(string provider = "gemini")
    {
        var name = KeyName(provider);
        try
        {
            var v = await SecureStorage.Default.GetAsync(name);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        catch { /* SecureStorage unavailable (e.g. unsigned Catalyst dev build) */ }
        var p = Preferences.Default.Get<string?>(name, null);
        return string.IsNullOrEmpty(p) ? null : p;
    }

    public async Task SetAsync(string value, string provider = "gemini")
    {
        var name = KeyName(provider);
        try { await SecureStorage.Default.SetAsync(name, value); return; }
        catch { }
        Preferences.Default.Set(name, value);
    }
}

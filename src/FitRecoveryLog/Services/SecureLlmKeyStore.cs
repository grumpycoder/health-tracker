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
    private const string Name = "gemini_api_key";

    public async Task<string?> GetAsync()
    {
        try
        {
            var v = await SecureStorage.Default.GetAsync(Name);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        catch { /* SecureStorage unavailable (e.g. unsigned Catalyst dev build) */ }
        var p = Preferences.Default.Get<string?>(Name, null);
        return string.IsNullOrEmpty(p) ? null : p;
    }

    public async Task SetAsync(string value)
    {
        try { await SecureStorage.Default.SetAsync(Name, value); return; }
        catch { }
        Preferences.Default.Set(Name, value);
    }
}

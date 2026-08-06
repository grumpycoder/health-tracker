namespace FitRecoveryLog.Application.Ai;

/// <summary>
/// Stores the LLM provider's API key. Behind a port so platform-specific secure storage (the
/// iOS Keychain) stays out of the application/adapters, and so the Settings screen and the
/// provider adapter share one source of truth for the key.
/// </summary>
public interface ILlmKeyStore
{
    /// <summary>The stored key, or null/empty when none is set.</summary>
    Task<string?> GetAsync();

    /// <summary>Store the key. An empty value clears it.</summary>
    Task SetAsync(string value);
}
